#!/usr/bin/env python3
"""skill-graph-hub: one graph for all skills, plugins and connectors.

Single source of truth = NODES + EDGES below. Commands:
  python3 graph.py build            write graph.json + GRAPH.md (mermaid)
  python3 graph.py route "<task>"   rank nodes for a task and print the chain
  python3 graph.py show <id>        node + in/out edges
  python3 graph.py check            validate edges, find unlinked nodes
  python3 graph.py sync             compare graph with skills installed on disk
Stdlib only.
"""
import json, math, os, re, sys, unicodedata, glob

HERE = os.path.dirname(os.path.abspath(__file__))
SKILLS_DIR = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

# id, cluster, kind, keywords (EN + VI). Installed skills also get their SKILL.md description scanned.
N = lambda i, c, k, kw: dict(id=i, cluster=c, kind=k, keywords=kw)
NODES = [
 # route / entry
 N("hub","route","entry","skill hub chon skill nhieu skill ket hop chain which skill nen dung skill nao"),
 N("find-skills","route","skill","find skill install skill extend capability tim skill cai skill co skill nao"),
 N("brainstorming","route","skill","brainstorm creative feature design before build y tuong yeu cau truoc khi lam them tinh nang"),
 # data
 N("eda","data","skill","dataset csv excel parquet profile explore data kham pha du lieu chat luong du lieu"),
 N("stats","data","skill","ab test significance p-value sample size causal hypothesis kiem dinh co mau"),
 N("p-data","data","plugin","sql warehouse query chart dashboard data story viet sql bieu do"),
 N("conn","data","connector","connector gmail drive slack crm calendar canva ket noi dich vu"),
 N("debug","build","skill","error traceback failing test wrong numbers odd result bug loi ket qua sai"),
 # web / browser
 N("agent-browser","web","skill","browser automation click form screenshot scrape test web app qa dogfood slack electron trinh duyet tu dong"),
 N("p-web","web","plugin","scrape pages google results structured extraction brightdata thu thap du lieu web"),
 N("seo-audit","web","skill","seo audit ranking traffic dropped crawl indexing kiem tra seo"),
 N("p-seo","web","plugin","seo content strategy keywords schema markup searchfit chien luoc noi dung"),
 N("fixing-metadata","web","skill","meta title description canonical open graph twitter card favicon json-ld robots"),
 N("web-perf","web","skill","core web vitals lighthouse lcp cls loading speed performance toi uu toc do"),
 # cloudflare
 N("cloudflare","cloudflare","skill","cloudflare product choose architecture storage networking security chon san pham"),
 N("wrangler","cloudflare","skill","wrangler cli deploy preview local dev config worker project"),
 N("workers-best-practices","cloudflare","skill","workers production review config best practice"),
 N("durable-objects","cloudflare","skill","durable objects persistent state coordination websocket"),
 N("agents-sdk","cloudflare","skill","cloudflare agents sdk ai agent stateful"),
 N("k2","cloudflare","skill","k2 streams durable log retention subscription produce consume"),
 N("basin","cloudflare","skill","basin pipelines catalog r2 iceberg sql analytics streaming"),
 N("cloudflare-email-service","cloudflare","skill","email sending routing delivery cloudflare"),
 N("cloudflare-one","cloudflare","skill","zero trust sase gateway access warp tunnel"),
 N("cloudflare-one-migrations","cloudflare","skill","migrate vpn swg sase policy mapping rollout chuyen doi"),
 N("turnstile-spin","cloudflare","skill","turnstile bot verification captcha siteverify"),
 N("nextjs-on-cloudflare","cloudflare","skill","nextjs next.js vinext opennext deploy workers"),
 N("sandbox-stable","cloudflare","skill","cloudflare sandbox stable sdk"),
 N("sandbox-next","cloudflare","skill","cloudflare sandbox next preview sdk 1.0"),
 N("sandbox-migrate-to-next","cloudflare","skill","migrate sandbox stable to next preview"),
 # redis
 N("redis-core","redis","skill","redis data model structure hash json key naming counter leaderboard session cache"),
 N("redis-search","redis","skill","redis search ft.create ft.search vector hnsw hybrid rag index"),
 N("redis-connections","redis","skill","redis client connection pool pipeline timeout scan resp3 client-side caching"),
 N("redis-clustering","redis","skill","redis cluster crossslot hash tag replica shard"),
 N("redis-security","redis","skill","redis acl tls auth requirepass hardening firewall"),
 N("redis-observability","redis","skill","redis monitoring slowlog info metrics alerts incident triage"),
 N("redis-semantic-cache","redis","skill","langcache semantic cache llm response similarity threshold cost latency"),
 N("iris-development","redis","skill","iris agent memory long-term memory session events redis cloud"),
 N("supabase-postgres-best-practices","postgres","skill","postgres sql schema migration rls index slow query pgvector pg_cron supabase explain database"),
 # frontend / ui
 N("frontend-design","ui","skill","visual design aesthetic typography distinctive ui giao dien dep thiet ke"),
 N("ui-skills-root","ui","skill","select ui skills context ui work"),
 N("baseline-ui","ui","skill","deslop spacing hierarchy typography layout polish cleanup"),
 N("improve-ui","ui","skill","audit ui design evidence drift plan handoff read-only"),
 N("create-design-md","ui","skill","design md design tokens design language visual system document"),
 N("fixing-accessibility","ui","skill","accessibility aria keyboard focus contrast wcag a11y"),
 N("fixing-motion-performance","ui","skill","animation jank layout thrashing compositor scroll motion"),
 N("web-design-guidelines","ui","skill","review ui web interface guidelines ux audit best practices"),
 N("vercel-react-best-practices","ui","skill","react next.js performance bundle data fetching rendering"),
 N("vercel-composition-patterns","ui","skill","react composition compound components render props boolean props api"),
 N("vercel-react-native-skills","mobile","skill","react native expo mobile list performance animation native modules"),
 N("sleek-design-mobile-apps","mobile","skill","sleek mobile app design screens swiftui html react native"),
 N("remotion-best-practices","video","skill","remotion video react programmatic animation render"),
 # design plugins
 N("p-design","ui","plugin","design critique design system ux writing accessibility audit research synthesis handoff"),
 N("p-figma","ui","plugin","figma file design tokens design to code"),
 # code / review
 N("code-review-expert","review","skill","code review git changes solid security senior engineer review code"),
 N("code-review","review","skill","review diff pr correctness bugs simplify"),
 N("p-eng","review","plugin","code review architecture decision incident response standup technical docs su co"),
 N("p-term","review","plugin","terminal commands processes files on computer chay lenh"),
 # learning / knowledge
 N("sigma","learn","skill","tutor learn teach me study topic socratic mastery hoc giai thich"),
 N("book-study","learn","skill","read book chapter ingest quiz spaced repetition reading plan doc sach"),
 N("wiki-ingest","learn","skill","wiki knowledge base ingest compile notes"),
 # output
 N("docs","output","skill","report memo proposal plan guide notes document viet tai lieu bao cao"),
 N("docx","output","skill","word docx tracked changes file word"),
 N("pdf","output","skill","pdf extract merge split fill form ocr watermark file pdf"),
 N("pptx","output","skill","powerpoint pptx slides deck trinh chieu"),
 N("xlsx","output","skill","spreadsheet xlsx csv excel bang tinh"),
 N("google-workspace","output","skill","google doc sheet slides drive workspace"),
 N("deliver","route","switch","write present convert deliverable file xuat file"),
 N("source","route","switch","where data comes from upload paste connector query scraped"),
 # meta
 N("skill-creator","meta","skill","create improve test skill evals benchmark tao skill"),
 N("skill-forge","meta","skill","create skill architecture workflow packaging skill.md"),
 N("skill-review","meta","skill","review audit lint skill quality anti-patterns"),
 N("session-start-hook","meta","skill","session start hook cloud sessions setup repo tests linters"),
 N("import-memory","meta","skill","import memory other assistant"),
 N("morning","meta","skill","morning brief ban tin buoi sang"),
]
STANDALONE = {"import-memory", "morning", "remotion-best-practices"}

E = lambda a, b, t, c="": dict(**{"from": a}, to=b, type=t, cond=c)
EDGES = [
 # entries
 E("hub","brainstorming","entry","creative or build work with unclear requirements"),
 E("hub","find-skills","entry","no node fits; look for an installable skill"),
 E("hub","source","entry","task starts from data"), E("hub","deliver","entry","task is only to write or convert"),
 E("hub","debug","entry","error or wrong result"), E("hub","stats","entry","hypothesis or A/B test"),
 E("hub","p-data","entry","SQL, charts, dashboards"), E("hub","cloudflare","entry","Cloudflare product or Workers task"),
 E("hub","redis-core","entry","Redis task"), E("hub","supabase-postgres-best-practices","entry","Postgres or Supabase task"),
 E("hub","ui-skills-root","entry","UI cleanup, audit or review"), E("hub","frontend-design","entry","build new UI"),
 E("hub","vercel-react-native-skills","entry","mobile app"), E("hub","agent-browser","entry","drive or test a website"),
 E("hub","seo-audit","entry","SEO problem"), E("hub","code-review-expert","entry","review changes"),
 E("hub","sigma","entry","learn a topic"), E("hub","book-study","entry","study a book"),
 E("hub","skill-forge","entry","create or improve a skill"), E("hub","session-start-hook","entry","set up a repo for cloud sessions"), E("hub","p-eng","entry","architecture, incidents"),
 E("hub","p-design","entry","design critique"), E("hub","p-figma","entry","Figma file"),
 E("hub","p-web","entry","scraping"), E("hub","p-term","entry","local files and commands"),
 E("hub","remotion-best-practices","entry","programmatic video"),
 # data
 E("p-web","source","feeds","collect external data"), E("conn","source","feeds","rows from a connector"),
 E("p-term","source","feeds","local files"), E("source","eda","feeds","dataset not yet profiled"),
 E("p-data","eda","feeds","SQL results to profile"), E("eda","stats","feeds","testable question"),
 E("eda","deliver","report","write findings"), E("stats","deliver","report","write results"),
 E("p-data","deliver","report","written summary"),
 E("any","debug","on-error","anything fails or looks wrong"),
 E("debug","p-eng","alt","production incident: p-eng; code or data bug: debug"),
 # output routing
 E("deliver","docs","route","default"), E("deliver","docx","route","Word named"), E("deliver","pdf","route","PDF named"),
 E("deliver","pptx","route","PowerPoint named"), E("deliver","xlsx","route","spreadsheet deliverable"),
 E("deliver","google-workspace","route","Google file named"),
 E("code-review-expert","deliver","report","write review"), E("p-eng","deliver","report","docs, ADRs, postmortems"),
 # code review
 E("code-review-expert","code-review","alt","either reviews a diff; expert = SOLID/security lens"),
 E("brainstorming","frontend-design","feeds","UI idea settled, now design it"),
 E("brainstorming","skill-forge","feeds","skill idea settled, now build it"),
 # cloudflare
 E("cloudflare","wrangler","route","deploy, config"), E("cloudflare","workers-best-practices","route","writing Workers"),
 E("cloudflare","durable-objects","route","state, coordination"), E("cloudflare","agents-sdk","route","AI agents"),
 E("cloudflare","k2","route","durable logs"), E("cloudflare","basin","route","analytics pipelines, R2 SQL"),
 E("cloudflare","cloudflare-email-service","route","email"), E("cloudflare","cloudflare-one","route","zero trust"),
 E("cloudflare","turnstile-spin","route","bot verification"), E("cloudflare","nextjs-on-cloudflare","route","Next.js on Workers"),
 E("cloudflare","sandbox-stable","route","sandbox apps"),
 E("cloudflare-one","cloudflare-one-migrations","alt","migrating from another vendor: migrations"),
 E("sandbox-stable","sandbox-next","alt","app on the preview SDK: next"),
 E("sandbox-stable","sandbox-migrate-to-next","alt","porting stable to preview: migrate"),
 E("sandbox-next","sandbox-migrate-to-next","alt","porting stable to preview: migrate"),
 E("workers-best-practices","wrangler","feeds","deploy and configure after writing"),
 E("nextjs-on-cloudflare","wrangler","feeds","deploy the app"),
 E("durable-objects","workers-best-practices","feeds","DO code lives in a Worker"),
 E("agents-sdk","durable-objects","feeds","agents run on Durable Objects"),
 E("basin","k2","alt","analytics tables: basin; raw durable log: k2"),
 E("web-perf","wrangler","handoff","ship perf fixes"),
 # redis
 E("redis-core","redis-search","route","search, vector, RAG"), E("redis-core","redis-connections","route","client config"),
 E("redis-core","redis-clustering","route","sharding, CROSSSLOT"), E("redis-core","redis-security","route","hardening"),
 E("redis-core","redis-observability","route","monitoring"), E("redis-core","redis-semantic-cache","route","LLM cache"),
 E("redis-core","iris-development","route","agent memory"),
 E("redis-connections","redis-observability","feeds","tune after measuring"),
 E("redis-search","redis-observability","feeds","profile slow queries"),
 E("redis-semantic-cache","redis-search","feeds","vector similarity underneath"),
 E("redis-core","supabase-postgres-best-practices","alt","relational data: Postgres; cache, counters, queues: Redis"),
 E("supabase-postgres-best-practices","debug","on-error","slow query, wrong rows visible"),
 # ui
 E("ui-skills-root","baseline-ui","route","quick polish"), E("ui-skills-root","improve-ui","route","evidence-based audit"),
 E("ui-skills-root","create-design-md","route","document design language"),
 E("ui-skills-root","fixing-accessibility","route","a11y"), E("ui-skills-root","fixing-motion-performance","route","animation jank"),
 E("ui-skills-root","fixing-metadata","route","page metadata"),
 E("create-design-md","frontend-design","feeds","design context for new UI"),
 E("frontend-design","vercel-react-best-practices","feeds","implement in React/Next.js"),
 E("frontend-design","vercel-composition-patterns","feeds","component API design"),
 E("frontend-design","baseline-ui","feeds","polish pass after building"),
 E("frontend-design","sleek-design-mobile-apps","alt","mobile screens: sleek; web: frontend-design"),
 E("sleek-design-mobile-apps","vercel-react-native-skills","feeds","implement designs in React Native"),
 E("improve-ui","web-design-guidelines","alt","audit by own evidence: improve-ui; by public guidelines: web-design-guidelines"),
 E("web-design-guidelines","fixing-accessibility","feeds","fix a11y findings"),
 E("web-design-guidelines","fixing-motion-performance","feeds","fix motion findings"),
 E("improve-ui","deliver","report","implementation plan"),
 E("p-design","p-figma","feeds","critique or handoff needs the file"), E("p-design","p-eng","handoff","specs to developers"),
 E("p-figma","frontend-design","feeds","design to code"),
 E("vercel-react-best-practices","web-perf","feeds","verify Core Web Vitals"),
 E("fixing-motion-performance","web-perf","feeds","verify interaction performance"),
 # web / seo
 E("seo-audit","fixing-metadata","feeds","fix metadata issues found"), E("seo-audit","web-perf","feeds","speed issues found"),
 E("seo-audit","deliver","report","audit document"), E("p-seo","p-web","feeds","competitor pages, SERPs"),
 E("p-seo","seo-audit","alt","strategy and keywords: p-seo; site diagnosis: seo-audit"),
 E("p-seo","deliver","report","strategy document"),
 E("agent-browser","p-web","alt","interactive or logged-in pages: agent-browser; bulk extraction: p-web"),
 E("agent-browser","source","feeds","scraped pages become data"),
 E("agent-browser","web-design-guidelines","feeds","test the running app, then review UI"),
 # learning
 E("sigma","book-study","alt","topic learning: sigma; a specific book: book-study"),
 E("sigma","wiki-ingest","feeds","save what was learned"), E("book-study","wiki-ingest","feeds","compile notes"),
 # meta
 E("skill-forge","skill-review","feeds","audit the new skill"), E("skill-creator","skill-review","feeds","audit the new skill"),
 E("skill-forge","skill-creator","alt","design guidance: forge; evals and benchmarks: creator"),
 E("skill-creator","hub","maintain","register new skill in graph.py"),
 E("skill-forge","hub","maintain","register new skill in graph.py"),
 E("find-skills","hub","maintain","register installed skill in graph.py"),
 E("remotion-best-practices","vercel-react-best-practices","alt","Remotion is React: reuse React guidance"),
]

# ---------- helpers ----------
def norm(s):
    s = unicodedata.normalize("NFD", s.lower().replace("đ", "d"))
    return "".join(c for c in s if unicodedata.category(c) != "Mn")
STOP = set("the a an and or to of for in on with is it this that use when user wants want be as by from at your my i me we you do how can".split())
def toks(s): return [t for t in re.findall(r"[a-z0-9.\-+_]+", norm(s)) if t not in STOP and len(t) > 1]

def read_desc(i):
    for base in (SKILLS_DIR, os.path.join(ROOT, ".claude", "skills")):
        p = os.path.join(base, i, "SKILL.md")
        if os.path.isfile(p):
            m = re.search(r"^---\n(.*?)\n---", open(p, errors="ignore").read(), re.S)
            d = re.search(r"^description:\s*(.*?)(?=\n[\w-]+:|\Z)", m.group(1) if m else "", re.S | re.M)
            return re.sub(r"\s+", " ", d.group(1)).strip("'\"> |") if d else ""
    return ""

def installed():
    s = {os.path.basename(os.path.dirname(p)) for p in glob.glob(os.path.join(SKILLS_DIR, "*", "SKILL.md"))}
    s |= {os.path.basename(os.path.dirname(p)) for p in glob.glob(os.path.expanduser("~/.claude/skills/**/SKILL.md"), recursive=True)}
    return s

def load():
    for n in NODES: n["description"] = read_desc(n["id"])
    return NODES, EDGES

# ---------- commands ----------
def check():
    ids = {n["id"] for n in NODES}; bad = 0
    if len(ids) != len(NODES): print("duplicate ids"); bad += 1
    linked = set()
    for e in EDGES:
        for k in ("from", "to"):
            if e[k] != "any" and e[k] not in ids: print("bad endpoint", e); bad += 1
        linked |= {e["from"], e["to"]}
    for i in sorted(ids - linked - STANDALONE): print("unlinked (mark standalone or add edge):", i); bad += 1
    print(f"{len(NODES)} nodes, {len(EDGES)} edges, {bad} problems"); return bad

def build():
    nodes, edges = load()
    inst = installed()
    for n in nodes: n["installed"] = n["id"] in inst or n["kind"] in ("switch", "entry")
    json.dump(dict(nodes=nodes, edges=edges), open(os.path.join(HERE, "graph.json"), "w"), ensure_ascii=False, indent=1)
    sty = {"feeds": "-->", "route": "==>", "alt": "<-.->", "report": "-.->", "handoff": "-->", "entry": "-->", "on-error": "-.->", "maintain": "-.->"}
    L = ["# Skill graph", "", f"{len(nodes)} nodes, {len(edges)} edges. Generated by `graph.py build`; edit graph.py, not this file.", "",
         "Edge types: `==>` route, `-->` feeds/entry/handoff, `<-.->` alt, `-.->` report/on-error/maintain.", "", "```mermaid", "flowchart LR"]
    cl = {}
    for n in nodes: cl.setdefault(n["cluster"], []).append(n)
    for c, ns in cl.items():
        L.append(f'  subgraph {c.replace("-","_")}["{c}"]')
        for n in ns: L.append(f'    {n["id"].replace("-","_").replace(".","_")}["{n["id"]}"]')
        L.append("  end")
    for e in edges:
        a = "ANY" if e["from"] == "any" else e["from"].replace("-", "_")
        L.append(f'  {a} {sty[e["type"]]}|{e["type"]}| {e["to"].replace("-","_")}')
    L.append("```")
    L += ["", "## Nodes by cluster", ""]
    for c, ns in cl.items():
        L.append(f"### {c}")
        for n in ns:
            tag = "" if n["installed"] else " (not installed here)"
            L.append(f"- **{n['id']}** [{n['kind']}]{tag}: {(n['description'] or n['keywords'])[:140]}")
        L.append("")
    open(os.path.join(HERE, "GRAPH.md"), "w").write("\n".join(L))
    print("wrote graph.json, GRAPH.md"); return check()

def route(q, k=3):
    nodes, edges = load()
    docs = {n["id"]: toks(n["id"].replace("-", " ") + " " + (n["keywords"] + " ") * 3 + n["description"]) for n in nodes}
    df = {}
    for d in docs.values():
        for t in set(d): df[t] = df.get(t, 0) + 1
    qt = set(toks(q)); sc = {}
    for i, d in docs.items():
        s = sum(math.log(1 + len(docs) / df[t]) * (1 + math.log(d.count(t))) for t in qt if t in df and t in d)
        if s > 0 and next(n for n in nodes if n["id"] == i)["kind"] not in ("switch", "entry"): sc[i] = s
    seeds = sorted(sc, key=sc.get, reverse=True)[:k]
    seeds = [s for s in seeds if sc[s] >= 0.8 * sc[seeds[0]]]
    if not seeds: print("no match; try find-skills"); return
    chain = []
    def add(i):
        if i not in chain: chain.append(i)
    for s in seeds:
        for e in edges:  # predecessors that feed the seed
            if e["to"] == s and e["type"] == "feeds" and e["from"] not in ("any", "hub") and sc.get(e["from"], 0) >= 0.5 * sc[seeds[0]]: add(e["from"])
        add(s)
    print("ranked:", ", ".join(f"{i}({sc[i]:.1f})" for i in seeds))
    print("chain :", " -> ".join(chain[:4]))
    for i in chain[:4]:
        out = [f'{e["type"]}:{e["to"]}' for e in edges if e["from"] == i and e["to"] not in ("hub",)][:5]
        print(f"  {i}: next options -> {', '.join(out) or '-'}")

def show(i):
    for e in EDGES:
        if i in (e["from"], e["to"]): print(f'{e["from"]} --{e["type"]}--> {e["to"]}  [{e["cond"]}]')

def sync():
    ids = {n["id"] for n in NODES}; inst = installed()
    print("installed but not in graph:", sorted(inst - ids) or "none")
    print("in graph, kind=skill, not installed here:", sorted(n["id"] for n in NODES if n["kind"] == "skill" and n["id"] not in inst))

if __name__ == "__main__":
    a = sys.argv[1:]
    {"build": lambda: build(), "check": lambda: check(), "sync": lambda: sync(),
     "route": lambda: route(" ".join(a[1:])), "show": lambda: show(a[1])}.get(a[0] if a else "", lambda: print(__doc__))()
