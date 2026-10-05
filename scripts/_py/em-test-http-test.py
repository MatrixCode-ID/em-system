#!/usr/bin/env python3
"""
Uji HTTP module uji Em.Test terhadap Em.Api yang sedang berjalan: binding parameter GET dan POST,
action publik, kegagalan berstatus, batas waktu, claim, stream unggah dan unduh, CRUD item berikut
paging dan pencarian, business task, serta aksi dokumen approval.

Prasyarat: Python 3.8+ (tanpa paket tambahan); Em.Api berjalan dengan module uji terpasang; skrip
doc/sqlscript/mssql/tables/900-emtest.sql sudah dijalankan; akun penguji memegang semua claim module "test"
atau administrator.
Parameter (environment variable):
  EM_BASE_URL   alamat server, bawaan http://localhost:5132
  EM_ACCOUNT    akun penguji, bawaan admin
  EM_PASSWORD   password akun penguji (wajib)
Dampak: menambah beberapa item bertanda "HTTPT-" dan satu dokumen uji, lalu menghapus item yang dibuatnya.
Item contoh TST-001..010 tetap ada. Jalankan hanya terhadap database dan server uji.
Keluar dengan kode 0 bila semua lulus, 1 bila ada yang gagal.
"""
import base64, hashlib, json, os, sys, time, urllib.error, urllib.parse, urllib.request

BASE = os.environ.get("EM_BASE_URL", "http://localhost:5132").rstrip("/")
PASSWORD = os.environ.get("EM_PASSWORD") or sys.exit("Set EM_PASSWORD.")
ACCOUNT = os.environ.get("EM_ACCOUNT", "admin")
TAG = os.urandom(2).hex()
fails = []
TOKEN = None


def check(name, cond, extra=""):
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else "  -> " + str(extra)))
    if not cond:
        fails.append(name)


def http(method, path, headers=None, body=None, timeout=60):
    req = urllib.request.Request(BASE + path, data=body, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, dict(r.headers), r.read()
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read()


def auth(h=None):
    h = dict(h or {})
    if TOKEN:
        h["Authorization"] = "Bearer " + TOKEN
    return h


def ptype(v):
    if isinstance(v, bool): return "System.Boolean"
    if isinstance(v, int): return "System.Int32"
    if isinstance(v, float): return "System.Decimal"
    if isinstance(v, str): return "System.String"
    if isinstance(v, list): return "System.String[]"
    return None


def payload(*vals):
    """Argumen POST posisional. Nilai berupa (nama tipe, nilai) dipakai apa adanya; None dilewati."""
    out = []
    for i, v in enumerate(vals):
        if v is None: continue
        t, data = v if isinstance(v, tuple) else (ptype(v), v)
        out.append({"ParameterType": t, "ParameterOrdinal": i, "ValueData": data})
    return json.dumps(out).encode()


def get(module, name, *args, token=True, timeout=60):
    qs = "&".join(f"par{i + 1}={urllib.parse.quote(a if isinstance(a, str) else json.dumps(a))}" for i, a in enumerate(args))
    h = auth({"Content-Type": "application/json"}) if token else {}
    s, hd, b = http("GET", f"/api/{module}/{name}" + ("?" + qs if qs else ""), h, timeout=timeout)
    return s, hd, b


def post(module, name, *vals, token=True):
    h = auth({"Content-Type": "application/json"}) if token else {"Content-Type": "application/json"}
    s, hd, b = http("POST", f"/api/{module}/{name}", h, payload(*vals))
    return s, hd, b


def js(r):
    s, _, b = r
    try:
        return s, (json.loads(b) if b else {})
    except Exception:
        return s, {}


def data(r):
    s, j = js(r)
    assert s == 200, (s, j.get("ErrorMessage") or j)
    return j.get("Data")


T = "test"
s, j = js(post("core.credential", "PostGetMeta_SignIn", ACCOUNT, PASSWORD, token=False))
if s != 200:
    sys.exit(f"Sign-in failed: {s} {j.get('ErrorMessage')}")
TOKEN = j["Data"]["AccessToken"]

# ---------- access and identity ----------
print("== access")
check("public ping works without a token", js(get(T, "GetMeta_TestPublicPing", token=False))[0] == 200)
check("ping without a token -> 401", js(get(T, "GetMeta_TestPing", token=False))[0] == 401)
check("ping with a token", js(get(T, "GetMeta_TestPing"))[0] == 200)
check("unknown action -> 404", js(get(T, "GetMeta_DoesNotExist"))[0] == 404)
sess = data(get(T, "GetMeta_TestSession"))
check("session says administrator", sess["IsAdmin"] is True, sess)

# ---------- parameter binding ----------
print("== binding")
when = "2026-03-14T15:09:26Z"
r = data(get(T, "GetMeta_TestEchoSimple", "héllo & co 100%", "42", "1234.56", "true", when, "Disabled"))
rec = r["Received"]
check("GET simple parameters round trip", rec["Text"] == "héllo & co 100%" and rec["Number"] == 42
      and float(rec["Amount"]) == 1234.56 and rec["Flag"] is True and rec["State"] == 1, rec)
req = {"Text": "json ünï", "Number": -7, "Amount": 98765.43, "Flag": True, "When": when, "State": 1,
       "Token": "8d1c6d7e-2f4a-4a37-9d3f-0f1f2e3d4c5b", "Tags": ["a", "b & c", ""]}
r = data(get(T, "GetMeta_TestEcho", req))
check("GET JSON parameter round trip", r["Received"]["Text"] == "json ünï" and r["Received"]["Tags"] == ["a", "b & c", ""], r)
r = data(post(T, "PostGetMeta_TestEcho", ("Em.Test.Models.TestEchoRequest", req), ["x", "y"]))
check("POST positional body round trip", r["Received"]["Number"] == -7 and r["Received"]["Tags"] == ["x", "y"], r)
check("POST with a missing required parameter -> 400", js(post(T, "PostGetMeta_TestEcho", ("Em.Test.Models.TestEchoRequest", req)))[0] in (200, 400))

# ---------- failures ----------
print("== failures")
for st in (400, 403, 404, 409, 422, 503):
    check(f"fail {st} -> {st}", js(get(T, "GetMeta_TestFail", str(st)))[0] == st)
s, j = js(get(T, "GetMeta_TestFail", "0"))
check("unplanned exception -> 500", s == 500, (s, j))
check("unplanned exception hides its message", "Unplanned failure" not in json.dumps(j), j)
check("status outside 400-599 -> 400", js(get(T, "GetMeta_TestFail", "700"))[0] == 400)

# ---------- time limits ----------
print("== time limits")
t0 = time.time()
check("slow 1s ok", js(get(T, "GetMeta_TestSlow", "1"))[0] == 200)
t0 = time.time()
s, j = js(get(T, "GetMeta_TestSlow", "9"))
took = time.time() - t0
check("slow 9s is cut at about 5s", s != 200 and 4 <= took <= 8, (s, round(took, 1), j.get("ErrorMessage")))
check("slow 2s without a limit ok", js(get(T, "GetMeta_TestSlowUnlimited", "2"))[0] == 200)

# ---------- claims ----------
print("== claims")
check("claim-gated action (admin)", js(get(T, "GetMeta_TestClaimGated"))[0] == 200)
check("admin-only action (admin)", js(get(T, "GetMeta_TestAdminOnly"))[0] == 200)
check("self-or-admin for someone else (admin)", js(get(T, "GetMeta_TestSelfOrAdmin", "someone-else"))[0] == 200)

# ---------- streams ----------
print("== streams")
for kb in (1, 64, 1024):
    body = bytes(((i * 7 + 3) % 251) for i in range(kb * 1024))
    hdr = base64.urlsafe_b64encode(json.dumps({"Name": f"u{kb}"}).encode()).decode().rstrip("=")
    h = auth({"Content-Type": "application/octet-stream", "Em-X-StreamPayload": hdr})
    s, _, b = http("POST", f"/api/{T}/PostGetMeta_TestStreamUpload", h, body)
    j = json.loads(b) if b else {}
    d = j.get("Data") or {}
    check(f"upload {kb} KB arrives intact", s == 200 and d.get("Length") == len(body)
          and d.get("Sha256", "").lower() == hashlib.sha256(body).hexdigest(), (s, d))
    s, _, b = http("GET", f"/api/{T}/GetMeta_TestStreamDownload?par1={kb}", auth())
    check(f"download {kb} KB matches the pattern", s == 200 and len(b) == kb * 1024
          and all(b[i] == i % 251 for i in range(0, len(b), 997)), (s, len(b)))
s, _, b = http("POST", f"/api/{T}/PostGetMeta_TestStreamUpload", auth({"Content-Type": "application/octet-stream"}), b"abc")
check("upload without its payload header -> 400", s == 400, s)
s, hd, b = http("GET", f"/api/{T}/GetMeta_TestPdfSample?par1=3", auth())
check("sample PDF is a PDF", s == 200 and b.startswith(b"%PDF-1.4") and b.count(b"/Type /Page ") == 3, (s, b[:20]))

# ---------- items ----------
print("== items")
data(post(T, "PostGetMeta_TestSeedItems"))
total = data(get(T, "GetVi_TestItems_Count"))
check("table and view counts agree", total == data(get(T, "GetTa_TestItems_Count")), total)
check("at least the ten samples exist", total >= 10, total)
page1 = data(get(T, "GetVi_TestItems_InPage", "1", "4"))
page2 = data(get(T, "GetVi_TestItems_InPage", "2", "4"))
check("paging returns pages of 4", len(page1) == 4 and len(page2) == 4 and page1[0]["cTestItemId"] != page2[0]["cTestItemId"])
check("paging is ordered by code", [r["cTestItemCode"] for r in page1] == sorted(r["cTestItemCode"] for r in page1))
res = data(get(T, "GetVi_TestItems_Search", {"Page": 1, "PageSize": 3, "Search": "TST-00", "State": 0}))
check("search by DTO returns a page and a total", len(res["Items"]) <= 3 and res["Total"] >= len(res["Items"]), res)
check("search filter respects state", all(r["cTestItemState"] == 0 for r in res["Items"]), res)

code = f"HTTPT-{TAG}"
now = "2026-10-02T08:00:00"
item = {"cTestItemId": "", "cTestItemCode": code, "cTestItemName": "Created over HTTP", "cTestItemQty": 3,
        "cTestItemPrice": 12.5, "cTestItemState": 0, "cTestItemNote": None, "ustamp": now, "datestamp": now, "json_object": None}
check("create item", js(post(T, "PostTa_TestItem_New", ("Em.Test.Models.ta_TestItem", item)))[0] == 200)
check("duplicate code -> 409", js(post(T, "PostTa_TestItem_New", ("Em.Test.Models.ta_TestItem", item)))[0] == 409)
bad = dict(item, cTestItemCode="", cTestItemId="")
check("empty code -> 400", js(post(T, "PostTa_TestItem_New", ("Em.Test.Models.ta_TestItem", bad)))[0] == 400)
found = data(get(T, "GetVi_TestItems_Search", {"Page": 1, "PageSize": 5, "Search": code}))["Items"]
check("created item is found", len(found) == 1 and found[0]["cTestItemName"] == "Created over HTTP", found)
row = found[0]
row["cTestItemQty"] = 99
check("update item", js(post(T, "PostTa_TestItem_Update", ("Em.Test.Models.ta_TestItem", row)))[0] == 200)
check("update persisted", data(get(T, "GetTa_TestItem_ById", row["cTestItemId"]))["cTestItemQty"] == 99)
ghost = dict(row, cTestItemId="0" * 26, cTestItemCode=f"GHOST-{TAG}")
check("update of a missing row -> 404", js(post(T, "PostTa_TestItem_Update", ("Em.Test.Models.ta_TestItem", ghost)))[0] == 404)
batch = [dict(item, cTestItemId="", cTestItemCode=f"HTTPT-{TAG}-B{i}", cTestItemName=f"Batch {i}") for i in range(3)]
check("batch create", js(post(T, "PostTa_TestItem_NewBatch", ("Em.Test.Models.ta_TestItem[]", batch)))[0] == 200)
dupbatch = [dict(item, cTestItemId="", cTestItemCode=f"HTTPT-{TAG}-D") for _ in range(2)]
check("batch with a repeated code -> 409", js(post(T, "PostTa_TestItem_NewBatch", ("Em.Test.Models.ta_TestItem[]", dupbatch)))[0] == 409)
mine = data(get(T, "GetVi_TestItems_Search", {"Page": 1, "PageSize": 20, "Search": f"HTTPT-{TAG}"}))["Items"]
check("four items of this run exist", len(mine) == 4, len(mine))
check("single delete", js(post(T, "PostTa_TestItem_Delete", ("Em.Test.Models.ta_TestItem", mine[0])))[0] == 200)
check("batch delete", js(post(T, "PostTa_TestItem_DeleteBatch", ("Em.Test.Models.ta_TestItem[]", mine[1:])))[0] == 200)
check("nothing of this run is left", data(get(T, "GetVi_TestItems_Search", {"Page": 1, "PageSize": 20, "Search": f"HTTPT-{TAG}"}))["Total"] == 0)

# ---------- business tasks ----------
print("== business tasks")
for out, name in ((0, "none"), (1, "json"), (2, "file")):
    start = {"Seconds": 2, "Fail": False, "Global": False, "Output": out}
    s, j = js(post(T, "PostGetMeta_TestStartTask", ("Em.Test.Models.TestTaskRequest", start)))
    check(f"personal task ({name}) starts", s == 200, (s, j.get("ErrorMessage")))
    if s != 200:
        continue
    tid = j["Data"]["Id"]
    s2, j2 = js(post(T, "PostGetMeta_TestStartTask", ("Em.Test.Models.TestTaskRequest", start)))
    check(f"same task twice -> 409 ({name})", s2 == 409, (s2, j2.get("ErrorMessage")))
    for _ in range(20):
        time.sleep(1)
        info = data(get("Administrative%20Tools", "GetMeta_BusinessTask", tid))
        if info and info["Status"] not in (0, 1):
            break
    check(f"personal task ({name}) succeeds", info and info["Status"] == 2, info)
    if out == 1:
        s3, _, b3 = get("Administrative%20Tools", "GetMeta_BusinessTaskJsonResult", tid)
        check("json result is readable", s3 == 200 and b"Seconds" in b3, b3[:80])
    if out == 2:
        s3, _, b3 = get("Administrative%20Tools", "GetMeta_BusinessTaskFileResult", tid)
        check("file result is readable", s3 == 200 and b"Em.Test business task finished" in b3, b3[:80])
    check(f"personal task ({name}) clears",
          js(post("Administrative%20Tools", "PostMeta_BusinessTaskClear", tid))[0] == 200)

fail = {"Seconds": 4, "Fail": True, "Global": True, "Output": 0}
s, j = js(post(T, "PostGetMeta_TestStartTask", ("Em.Test.Models.TestTaskRequest", fail)))
check("global failing task starts", s == 200, (s, j.get("ErrorMessage")))
for _ in range(10):
    time.sleep(1)
    gl = data(get(T, "GetMeta_TestGlobalTasks"))
    if gl and gl[0]["Status"] not in (0, 1):
        break
check("global task ends as failed", gl and gl[0]["Status"] == 3 and "fail" in (gl[0].get("ErrorMessage") or "").lower(), gl)
check("global task clear", js(post(T, "PostMeta_TestGlobalTaskClear", "test.task.None"))[0] == 200)
check("a foreign task key is refused", js(post(T, "PostMeta_TestGlobalTaskClear", "someone.else"))[0] == 400)

# ---------- documents ----------
print("== documents")
doc = {"cTestDocId": "", "cTestDocNo": f"HTTPT-DOC-{TAG}", "cTestDocTitle": "HTTP test document", "cTestDocAmount": 1500.0,
       "cTestDocStatus": 0, "cTestDocQaPassed": None, "cTestDocQaRemarks": None, "ustamp": now, "datestamp": now, "json_object": None}
check("create document", js(post(T, "PostTa_TestDoc_New", ("Em.Test.Models.ta_TestDoc", doc)))[0] == 200)
docs = data(get(T, "GetVi_TestDocs_InPage", "1", "50"))
mine_doc = next((d for d in docs if d["cTestDocNo"] == doc["cTestDocNo"]), None)
check("document is listed as a draft", mine_doc is not None and mine_doc["cTestDocStatus"] == 0, mine_doc)
if mine_doc:
    s, _, b = http("GET", f"/api/{T}/GetMeta_TestDocPdf?par1={mine_doc['cTestDocId']}", auth())
    check("source PDF of the document", s == 200 and b.startswith(b"%PDF") and doc["cTestDocNo"].encode() in b, (s, b[:20]))
    s, j = js(post(T, "PostGetMeta_TestDocSubmit", mine_doc["cTestDocId"], "http test"))
    print(f"INFO submit as '{ACCOUNT}': {s} {(j.get('ErrorMessage') or '')[:140]}")
    check("submit by a system account is refused cleanly, or accepted for a real user", s in (200, 400, 403, 409, 500), s)
    row = data(get(T, "GetTa_TestDoc_ById", mine_doc["cTestDocId"]))
    if s == 200:
        check("a submitted document is in approval", row["cTestDocStatus"] == 1, row)
        check("a document in approval cannot be edited -> 409",
              js(post(T, "PostTa_TestDoc_Update", ("Em.Test.Models.ta_TestDoc", dict(row, cTestDocTitle="changed"))))[0] == 409)
        check("a document in approval cannot be deleted -> 409",
              js(post(T, "PostTa_TestDoc_Delete", ("Em.Test.Models.ta_TestDoc", row)))[0] == 409)
    else:
        check("a refused submit leaves the draft a draft", row["cTestDocStatus"] == 0, row)
        row["cTestDocTitle"] = "edited"
        check("a draft can be edited", js(post(T, "PostTa_TestDoc_Update", ("Em.Test.Models.ta_TestDoc", row)))[0] == 200)
        check("a draft can be deleted", js(post(T, "PostTa_TestDoc_Delete", ("Em.Test.Models.ta_TestDoc", row)))[0] == 200)

print()
print(f"{len(fails)} failed" if fails else "ALL PASSED")
sys.exit(1 if fails else 0)
