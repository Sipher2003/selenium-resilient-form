using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

public record Cand(IWebElement El, Dictionary<string, string> F);

/// <summary>
/// Finds form controls by what they MEAN (label, aria, placeholder, nearby text, name/id)
/// rather than by a fixed XPath/CSS selector.
/// </summary>
public class SmartFinder
{
    readonly IWebDriver d;
    readonly IJavaScriptExecutor js;

    public SmartFinder(IWebDriver driver) { d = driver; js = (IJavaScriptExecutor)driver; }

    public void WaitForPage() =>
        new WebDriverWait(d, TimeSpan.FromSeconds(15))
            .Until(x => js.ExecuteScript("return document.readyState")!.Equals("complete"));

    // ---- text normalisation / matching ----
    public static string Norm(string? s) =>
        Regex.Replace(Regex.Replace(s ?? "", "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    static bool Has(string? text, string phrase)
    {
        var n = Norm(text); var p = Norm(phrase);
        if (n.Length == 0) return false;
        if ((" " + n + " ").Contains(" " + p + " ")) return true;
        // ids like "firstname" / "txtfirstname" (few tokens only)
        return n.Split(' ').Length <= 2 && n.Replace(" ", "").Contains(p.Replace(" ", ""));
    }

    static readonly Dictionary<string, int> Weights = new()
    {
        ["label"] = 10, ["aria"] = 9, ["nearby"] = 8, ["placeholder"] = 7,
        ["preceding"] = 6, ["name"] = 4, ["id"] = 4, ["title"] = 3
    };

    static int Score(Dictionary<string, string> f, string[] pos, string[] neg)
    {
        int s = 0;
        foreach (var (k, w) in Weights)
        {
            if (!f.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            if (pos.Any(p => Has(v, p))) s += w;
            if (neg.Any(n => Has(v, n))) s -= w * 2;
        }
        return s;
    }

    // ---- one JS call gathers elements + all "human" clues ----
    const string Collect = @"
const kind=arguments[0];
const clean=s=>(s||'').replace(/\s+/g,' ').trim();
const vis=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return s.visibility!=='hidden'&&s.display!=='none'&&r.width>0&&r.height>0;};
const CTRL='input:not([type=hidden]),select,textarea';
const textWithout=n=>{const c=n.cloneNode(true);c.querySelectorAll('select,option,script,style,input,textarea').forEach(x=>x.remove());return clean(c.textContent);};
function labelText(e){const p=[];if(e.labels)for(const l of e.labels)p.push(textWithout(l));
  const ids=e.getAttribute('aria-labelledby');if(ids)ids.split(/\s+/).forEach(i=>{const n=document.getElementById(i);if(n)p.push(textWithout(n));});
  return clean(p.join(' '));}
function nearby(e){let n=e.parentElement;for(let i=0;i<3&&n&&n!==document.body;i++,n=n.parentElement){
  if(n.querySelectorAll(CTRL).length>1)break;const t=textWithout(n);if(t&&t.length<60)return t;}return '';}
function preceding(e){let n=e;for(let i=0;i<3&&n&&n!==document.body;i++,n=n.parentElement){
  const s=n.previousElementSibling;if(s&&!s.querySelector(CTRL)){const t=textWithout(s);if(t&&t.length<60)return t;}}return '';}
function optText(e){let t=labelText(e);if(t)return t;const p=e.parentElement;
  if(p&&p.querySelectorAll('input[type=radio]').length===1){t=textWithout(p);if(t)return t;}
  let s=e.nextSibling;while(s&&!clean(s.textContent))s=s.nextSibling;return s?clean(s.textContent).slice(0,40):'';}
function groupCtx(e){const all=[...document.querySelectorAll('input[type=radio]')].filter(r=>r.name&&r.name===e.name);
  let n=e.parentElement;while(n&&n!==document.body&&!all.every(r=>n.contains(r)))n=n.parentElement;
  const out=[e.name||''];
  for(let i=0;i<3&&n&&n!==document.body;i++,n=n.parentElement){
    const t=textWithout(n);if(t.length<150)out.push(t);
    const s=n.previousElementSibling;if(s&&!s.querySelector(CTRL)){const u=textWithout(s);if(u.length<60)out.push(u);}}
  return out.join(' | ');}
let els;
if(kind==='text') els=[...document.querySelectorAll('input')].filter(e=>/^(text|search|)$/i.test(e.getAttribute('type')||'')&&vis(e)&&!e.disabled);
else if(kind==='select') els=[...document.querySelectorAll('select')].filter(e=>vis(e)&&!e.disabled);
else els=[...document.querySelectorAll('input[type=radio]')];
return els.map(e=>({el:e,label:labelText(e),aria:e.getAttribute('aria-label')||'',placeholder:e.getAttribute('placeholder')||'',
  title:e.getAttribute('title')||'',name:e.getAttribute('name')||'',id:e.id||'',nearby:nearby(e),preceding:preceding(e),
  option:kind==='radio'?optText(e):'',value:e.getAttribute('value')||'',group:kind==='radio'?groupCtx(e):''}));";

    List<Cand> Gather(string kind)
    {
        var raw = (System.Collections.ObjectModel.ReadOnlyCollection<object>)js.ExecuteScript(Collect, kind)!;
        return raw.Cast<Dictionary<string, object>>().Select(m => new Cand(
            (IWebElement)m["el"],
            m.Where(kv => kv.Key != "el").ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? ""))).ToList();
    }

    public Cand Best(string kind, string[] pos, string[] neg)
    {
        Cand? best = null;
        string diag = "";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {   // poll: page may render fields late
            var scored = Gather(kind).Select(c => (c, s: Score(c.F, pos, neg))).OrderByDescending(x => x.s).ToList();
            if (scored.Count > 0 && scored[0].s > 0) { best = scored[0].c; break; }
            diag = string.Join("; ", scored.Take(3).Select(x =>
                $"[{x.s}] {string.Join("/", x.c.F.Where(f => f.Value != "").Select(f => f.Value).Take(3))}"));
            Thread.Sleep(500);
        } while (DateTime.UtcNow < deadline);
        return best ?? throw new NoSuchElementException(
            $"No {kind} field matched {string.Join(",", pos)}. Top candidates: {diag}");
    }

    public Cand BestRadio(string wanted)
    {
        var cands = Gather("radio");
        var scored = cands.Select(c =>
        {
            int s = 0;
            if (Norm(c.F["option"]) == Norm(wanted)) s += 12;
            else if (Has(c.F["option"], wanted)) s += 8;
            if (Has(c.F["value"], wanted)) s += 3;
            if (Has(c.F["aria"], wanted) || Has(c.F["label"], wanted)) s += 2;
            if (s == 0) return (c, s: -1);
            if (Has(c.F["group"], "gender") || Has(c.F["group"], "sex")) s += 10; // right question, not any radio group
            return (c, s);
        }).OrderByDescending(x => x.s).ToList();
        if (scored.Count == 0 || scored[0].s <= 0)
            throw new NoSuchElementException($"No radio option '{wanted}' found");
        return scored[0].c;
    }
}
