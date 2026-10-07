#!/usr/bin/env python3
"""
HTTP test of the Em container registry (the /v2 path and the management services), imitating a Docker
client: chunked blob upload, cross-root mount, manifests, tags, Range, per-root grants, and their
negative cases.

Prerequisites: Python 3.8+ (no extra packages); Em.Api running with the registry on and the ta_Ctn*
tables created (doc/sqlscript/mssql/tables/030-registry.sql); an account that may sign in and holds the claims
"Administrative Tools:Container Manager Access" and "Administrative Tools:User Manager Access" (or is an administrator).
Parameters (environment variables):
  EM_BASE_URL   the server address, default http://localhost:5055
  EM_ACCOUNT    the test account, default admin
  EM_PASSWORD   the test account's password (required)
Impact: creates two roots, folders, containers, and robots with a RANDOM SUFFIX (e.g. server-a1b2), pushes
a few MB to the registry disk, then deletes what it created. Blobs that were uploaded stay on disk until
garbage collection runs. Run only against a test database and server.
Exits with code 0 when everything passes, 1 when anything fails.
"""
import json, hashlib, base64, urllib.request, urllib.error, sys, os

BASE = os.environ.get("EM_BASE_URL", "http://localhost:5055").rstrip("/")
PASSWORD = os.environ.get("EM_PASSWORD") or sys.exit("Set EM_PASSWORD.")
ACCOUNT = os.environ.get("EM_ACCOUNT", "admin")
TAG = os.urandom(2).hex()
S, O = f"server-{TAG}", f"acme-{TAG}"
NAMES = {"server": S, "acme": O}
ROBOTS = {n: f"{n}-{TAG}" for n in ("platform-ci", "acme-ci", "acme-dev", "other", "expired")}
fails = []
def check(name, cond, extra=""):
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else "  -> " + str(extra)))
    if not cond: fails.append(name)

def http(method, path, headers=None, body=None):
    path = path.replace("server/", S + "/").replace("acme/", O + "/")
    req = urllib.request.Request(BASE + path, data=body, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req) as r: return r.status, dict(r.headers), r.read()
    except urllib.error.HTTPError as e: return e.code, dict(e.headers), e.read()

TOKEN = None
def payload(*vals):
    types = {str: "System.String", bool: "System.Boolean"}
    out = []
    for i, v in enumerate(vals):
        if v is None: continue
        if isinstance(v, str): v = NAMES.get(v, ROBOTS.get(v, v))
        out.append({"ParameterType": types[type(v)] if type(v) in types else "System.DateTime", "ParameterOrdinal": i, "ValueData": v})
    return json.dumps(out).encode()

def act(module, name, *vals, get=False, expect=200):
    h = {"Content-Type": "application/json"}
    if TOKEN: h["Authorization"] = "Bearer " + TOKEN
    if get:
        qs = "&".join(f"{k}={v}" for k, v in vals[0].items()) if vals else ""
        s, _, b = http("GET", f"/api/{module}/{name}" + ("?" + qs if qs else ""), h)
    else:
        s, _, b = http("POST", f"/api/{module}/{name}", h, payload(*vals))
    j = json.loads(b) if b else {}
    return s, j

M = "Administrative%20Tools"
def mg(name, *vals, **kw): return act(M, name, *vals, **kw)

s, j = act("core.credential", "PostGetMeta_SignIn", ACCOUNT, PASSWORD)
TOKEN = j["Data"]["AccessToken"]

def data(r):
    s, j = r
    assert s == 200, (s, j)
    return j.get("Data")

# --- management ---
r_server = data(mg("PostGetMeta_CtnRootCreate", "server", "base images"))
r_acme = data(mg("PostGetMeta_CtnRootCreate", "acme", None))
check("root dup -> 409", mg("PostGetMeta_CtnRootCreate", "server", None)[0] == 409)
check("root uppercase -> 400", mg("PostGetMeta_CtnRootCreate", "Server", None)[0] == 400)
check("root too long -> 400", mg("PostGetMeta_CtnRootCreate", "a" * 65, None)[0] == 400)
f1 = data(mg("PostGetMeta_CtnFolderCreate", r_acme["Id"], None, "Apps"))
f2 = data(mg("PostGetMeta_CtnFolderCreate", r_acme["Id"], f1["Id"], "Backend"))
check("folder dup sibling -> 409", mg("PostGetMeta_CtnFolderCreate", r_acme["Id"], f1["Id"], "backend")[0] == 409)
# depth: f1(1) f2(2) ... up to 8
cur = f2
for i in range(3, 9): cur = data(mg("PostGetMeta_CtnFolderCreate", r_acme["Id"], cur["Id"], f"L{i}"))
check("folder depth 9 -> 400", mg("PostGetMeta_CtnFolderCreate", r_acme["Id"], cur["Id"], "L9")[0] == 400)
check("folder move into self child -> 400", mg("PostGetMeta_CtnFolderMove", f1["Id"], f2["Id"])[0] == 400)
img_base = data(mg("PostGetMeta_CtnImageCreate", r_server["Id"], None, "base", None))
img_api = data(mg("PostGetMeta_CtnImageCreate", r_acme["Id"], f2["Id"], "api.v2", "api"))
check("image dup -> 409", mg("PostGetMeta_CtnImageCreate", r_acme["Id"], None, "api.v2", None)[0] == 409)
check("image uppercase -> 400", mg("PostGetMeta_CtnImageCreate", r_acme["Id"], None, "Api", None)[0] == 400)
check("image too long -> 400", mg("PostGetMeta_CtnImageCreate", r_acme["Id"], None, "a" * 129, None)[0] == 400)
check("image slash -> 400", mg("PostGetMeta_CtnImageCreate", r_acme["Id"], None, "a/b", None)[0] == 400)

def robot(name):
    t = data(mg("PostGetMeta_RobotCreate", name, None))
    return t["Robot"]["Name"], t["Token"], t["Robot"]["Id"]
plat = robot("platform-ci"); acme_ci = robot("acme-ci"); acme_dev = robot("acme-dev"); other = robot("other")
check("robot dup -> 409", mg("PostGetMeta_RobotCreate", "acme-ci", None)[0] == 409)
check("robot bad name -> 400", mg("PostGetMeta_RobotCreate", "Bad Name", None)[0] == 400)
def grant(rb, root, acc): data(mg("PostMeta_RobotAccessSet", rb[2], "Container", root["Id"], acc))
grant(plat, r_server, "W"); grant(acme_ci, r_acme, "W"); grant(acme_ci, r_server, "R")
grant(acme_dev, r_acme, "R"); grant(acme_dev, r_server, "R")
check("bad access -> 400", mg("PostMeta_RobotAccessSet", plat[2], "Container", r_server["Id"], "X")[0] == 400)
check("root delete with content -> 409", mg("PostMeta_CtnRootDelete", r_acme["Id"])[0] == 409)
check("folder delete non-empty -> 409", mg("PostMeta_CtnFolderDelete", f2["Id"])[0] == 409)

# --- registry client ---
def auth(rb, token=None):
    return {"Authorization": "Basic " + base64.b64encode(f"{rb[0]}:{token or rb[1]}".encode()).decode()}
def sha(b): return "sha256:" + hashlib.sha256(b).hexdigest()
def err(body):
    try: return json.loads(body)["errors"][0]["code"]
    except Exception: return None

s, h, b = http("GET", "/v2/")
check("ping no auth -> 401 + challenge", s == 401 and "Basic" in h.get("WWW-Authenticate", ""), (s, h))
s, h, b = http("GET", "/v2/", auth(plat))
check("ping auth -> 200", s == 200 and h.get("Docker-Distribution-API-Version") == "registry/2.0", s)
s, _, _ = http("GET", "/v2/", auth(plat, "emc_wrongtoken"))
check("ping wrong token -> 401", s == 401)
s, _, _ = http("GET", "/v2/", auth(("platform-cj", plat[1])))
check("ping wrong username -> 401", s == 401)

def push_blob(rb, name, content, chunked=True):
    d = sha(content)
    s, h, b = http("POST", f"/v2/{name}/blobs/uploads/", auth(rb))
    if s != 202: return s, b
    loc = h["Location"]
    if chunked and len(content) > 10:
        half = len(content) // 2
        s, h, b = http("PATCH", loc, {**auth(rb), "Content-Type": "application/octet-stream"}, content[:half])
        if s != 202: return s, b
        s, h, b = http("PATCH", h["Location"], {**auth(rb), "Content-Type": "application/octet-stream", "Content-Range": f"{half}-{len(content)-1}"}, content[half:])
        if s != 202: return s, b
        loc = h["Location"]
        s, h, b = http("PUT", loc + "?digest=" + d, auth(rb))
    else:
        s, h, b = http("PUT", loc + "?digest=" + d, {**auth(rb), "Content-Type": "application/octet-stream"}, content)
    return s, b

layer = os.urandom(300000)
config = json.dumps({"architecture": "amd64", "os": "linux", "rootfs": {"type": "layers", "diff_ids": []}}).encode()

# push to name that does not exist
s, h, b = http("POST", "/v2/server/nope/blobs/uploads/", auth(plat))
check("push unknown name -> 404 NAME_UNKNOWN", s == 404 and err(b) == "NAME_UNKNOWN", (s, b))
s, h, b = http("POST", "/v2/nolroot/x/blobs/uploads/", auth(plat))
check("push unknown root -> 404 NAME_UNKNOWN", s == 404 and err(b) == "NAME_UNKNOWN", (s, b))
s, h, b = http("POST", "/v2/host/a/b/blobs/uploads/", auth(plat))
check("3 segments -> NAME_INVALID", s == 404 and err(b) == "NAME_INVALID", (s, b))
s, h, b = http("POST", "/v2/a/b/c/d/blobs/uploads/", auth(plat))
check("4 segments -> NAME_INVALID", s == 404 and err(b) == "NAME_INVALID", (s, b))
s, h, b = http("POST", "/v2/Server/Base/blobs/uploads/", auth(plat))
check("uppercase name -> NAME_INVALID", s == 404 and err(b) == "NAME_INVALID", (s, b))
s, h, b = http("POST", "/v2/only/blobs/uploads/", auth(plat))
check("1 segment -> NAME_INVALID", s == 404 and err(b) == "NAME_INVALID", (s, b))

# R robot cannot push to server; no-access robot sees NAME_UNKNOWN
s, b = push_blob(acme_ci, "server/base", layer)
check("acme-ci (R) push to server -> 403", s == 403 and err(b) == "DENIED", (s, b))
s, h, b = http("POST", "/v2/server/base/blobs/uploads/", auth(other))
check("robot without grant -> NAME_UNKNOWN (root hidden)", s == 404 and err(b) == "NAME_UNKNOWN", (s, b))

# platform-ci pushes server/base
s, b = push_blob(plat, "server/base", layer, chunked=True)
check("push chunked layer 201", s == 201, (s, b))
s, b = push_blob(plat, "server/base", config, chunked=False)
check("push monolithic-ish config 201", s == 201, (s, b))
# digest mismatch
s, h, b = http("POST", "/v2/server/base/blobs/uploads/", auth(plat))
s, h2, b = http("PUT", h["Location"] + "?digest=" + sha(b"x"), {**auth(plat)}, b"not x")
check("digest mismatch -> 400 DIGEST_INVALID", s == 400 and err(b) == "DIGEST_INVALID", (s, b))
# monolithic POST ?digest
mono = os.urandom(1000)
s, h, b = http("POST", "/v2/server/base/blobs/uploads/?digest=" + sha(mono), auth(plat), mono)
check("monolithic POST ?digest -> 201", s == 201, (s, b))

manifest = json.dumps({"schemaVersion": 2, "mediaType": "application/vnd.docker.distribution.manifest.v2+json",
    "config": {"mediaType": "application/vnd.docker.container.image.v1+json", "size": len(config), "digest": sha(config)},
    "layers": [{"mediaType": "application/vnd.docker.image.rootfs.diff.tar.gzip", "size": len(layer), "digest": sha(layer)}]}).encode()
MT = {"Content-Type": "application/vnd.docker.distribution.manifest.v2+json"}
# manifest referencing unlinked blob
bad = json.dumps({"schemaVersion": 2, "mediaType": MT["Content-Type"], "config": {"digest": sha(b"zz"), "size": 2}, "layers": []}).encode()
s, h, b = http("PUT", "/v2/server/base/manifests/latest", {**auth(plat), **MT}, bad)
check("manifest with unknown blob -> 400 MANIFEST_BLOB_UNKNOWN", s == 400 and err(b) == "MANIFEST_BLOB_UNKNOWN", (s, b))
s, h, b = http("PUT", "/v2/server/base/manifests/Latest!", {**auth(plat), **MT}, manifest)
check("bad tag -> 400", s == 400, (s, b))
s, h, b = http("PUT", "/v2/server/base/manifests/latest", {**auth(plat), **MT}, manifest)
check("manifest PUT 201 + digest", s == 201 and h.get("Docker-Content-Digest") == sha(manifest), (s, h, b))
s, h, b = http("PUT", "/v2/server/base/manifests/Latest", {**auth(plat), **MT}, manifest)
check("tag 'Latest' distinct from 'latest' (case-sensitive)", s == 201, (s, b))
s, h, b = http("PUT", "/v2/server/base/manifests/" + sha(b"other"), {**auth(plat), **MT}, manifest)
check("manifest digest ref mismatch -> 400", s == 400 and err(b) == "DIGEST_INVALID", (s, b))
s, h, b = http("GET", "/v2/server/base/manifests/latest", auth(acme_dev))
check("pull manifest by tag (R robot)", s == 200 and b == manifest and h.get("Docker-Content-Digest") == sha(manifest), (s,))
s, h, b = http("GET", "/v2/server/base/manifests/" + sha(manifest), auth(acme_dev))
check("pull manifest by digest", s == 200 and b == manifest)
s, h, b = http("HEAD", "/v2/server/base/manifests/latest", auth(acme_dev))
check("HEAD manifest", s == 200 and h.get("Content-Length") == str(len(manifest)), (s, h))
s, h, b = http("GET", "/v2/server/base/manifests/nope", auth(acme_dev))
check("unknown tag -> 404 MANIFEST_UNKNOWN", s == 404 and err(b) == "MANIFEST_UNKNOWN", (s, b))
s, h, b = http("GET", "/v2/server/base/tags/list", auth(acme_dev))
check("tags list", s == 200 and json.loads(b) == {"name": S + "/base", "tags": ["Latest", "latest"]}, b)
s, h, b = http("GET", "/v2/server/base/tags/list?n=1", auth(acme_dev))
check("tags list paging", s == 200 and json.loads(b)["tags"] == ["Latest"] and "rel=\"next\"" in h.get("Link", ""), (b, h))
s, h, b = http("GET", "/v2/server/base/blobs/" + sha(layer), auth(acme_dev))
check("pull layer (R robot)", s == 200 and b == layer, s)
s, h, b = http("GET", "/v2/server/base/blobs/" + sha(layer), {**auth(acme_dev), "Range": "bytes=10-19"})
check("blob Range -> 206", s == 206 and b == layer[10:20], (s, len(b)))
s, h, b = http("HEAD", "/v2/server/base/blobs/" + sha(layer), auth(acme_dev))
check("HEAD blob", s == 200 and h.get("Content-Length") == str(len(layer)), (s, h))
s, h, b = http("GET", "/v2/server/base/blobs/sha256:xyz", auth(acme_dev))
check("bad digest -> 400", s == 400, (s, b))
s, h, b = http("GET", "/v2/server/base/manifests/latest", auth(other))
check("robot w/o grant cannot pull", s == 404, s)
s, h, b = http("PUT", "/v2/server/base/manifests/hack", {**auth(acme_dev), **MT}, manifest)
check("R robot cannot push manifest -> 403", s == 403, s)

# acme-ci: layer of server/base is not visible via acme/api until mounted
s, h, b = http("GET", "/v2/acme/api.v2/blobs/" + sha(layer), auth(acme_ci))
check("blob not linked to acme/api -> 404", s == 404 and err(b) == "BLOB_UNKNOWN", (s, b))
s, h, b = http("HEAD", "/v2/acme/api.v2/blobs/" + sha(layer), auth(acme_ci))
check("HEAD unlinked blob -> 404", s == 404, s)
# manifest cannot reference unlinked layer
s, h, b = http("PUT", "/v2/acme/api.v2/manifests/v1", {**auth(acme_ci), **MT}, manifest)
check("manifest w/ unlinked layer -> 400", s == 400 and err(b) == "MANIFEST_BLOB_UNKNOWN", (s, b))
# mount from root where robot has no grant -> falls back to upload session (no leak)
s, h, b = http("POST", f"/v2/acme/api.v2/blobs/uploads/?mount={sha(layer)}&from=server/base", auth(other))
check("mount by robot w/o grant -> NAME_UNKNOWN", s == 404, s)
s, h, b = http("POST", f"/v2/acme/api.v2/blobs/uploads/?mount={sha(layer)}&from=acme/api.v2", auth(acme_dev))
check("mount R robot -> 403 (push needs W)", s == 403, s)
# real mount across roots (acme-ci has R on server)
s, h, b = http("POST", f"/v2/acme/api.v2/blobs/uploads/?mount={sha(layer)}&from=server/base", auth(acme_ci))
check("cross-root mount -> 201 (no re-upload)", s == 201 and h.get("Docker-Content-Digest") == sha(layer), (s, h))
s, h, b = http("GET", "/v2/acme/api.v2/blobs/" + sha(layer), auth(acme_ci))
check("mounted blob readable via acme/api", s == 200 and b == layer, s)
# mount of unknown digest falls back to upload
s, h, b = http("POST", f"/v2/acme/api.v2/blobs/uploads/?mount={sha(b'nothing')}&from=server/base", auth(acme_ci))
check("mount unknown digest -> 202 upload session", s == 202 and "Location" in h, (s,))
s, b = push_blob(acme_ci, "acme/api.v2", config, chunked=False)
check("acme-ci push config", s == 201, (s, b))
s, h, b = http("PUT", "/v2/acme/api.v2/manifests/v1", {**auth(acme_ci), **MT}, manifest)
check("acme-ci manifest PUT after mount", s == 201, (s, b))
# acme-ci still cannot push to server
s, h, b = http("PUT", "/v2/server/base/manifests/evil", {**auth(acme_ci), **MT}, manifest)
check("acme-ci cannot push to server", s == 403, s)

# upload status / cancel
s, h, b = http("POST", "/v2/acme/api.v2/blobs/uploads/", auth(acme_ci))
loc = h["Location"]
s, h2, b = http("PATCH", loc, auth(acme_ci), b"abcde")
s, h3, b = http("GET", loc, auth(acme_ci))
check("upload status 204 Range 0-4", s == 204 and h3.get("Range") == "0-4", (s, h3))
s, _, b = http("PATCH", loc, {**auth(acme_ci), "Content-Range": "99-100"}, b"zz")
check("PATCH wrong Content-Range -> 416", s == 416, s)
s, _, b = http("GET", loc, auth(acme_dev))
check("other robot cannot see upload", s in (403, 404), s)
s, _, b = http("DELETE", loc, auth(acme_ci))
check("cancel upload 204", s == 204, s)
s, _, b = http("GET", loc, auth(acme_ci))
check("cancelled upload gone", s == 404, s)

# move image: pull unaffected
r = mg("PostGetMeta_CtnImageMove", img_api["Id"], f1["Id"])
check("image move ok", r[0] == 200 and r[1]["Data"]["FullName"] == O + "/api.v2" and r[1]["Data"]["FolderId"] == f1["Id"], r)
s, h, b = http("GET", "/v2/acme/api.v2/manifests/v1", auth(acme_dev))
check("pull after move unchanged", s == 200 and b == manifest, s)
r = mg("PostGetMeta_CtnImageMove", img_api["Id"], None)
check("image move to root ok", r[0] == 200 and r[1]["Data"]["FolderId"] is None, r)

# manifests listing + tree
r = data(mg("GetMeta_CtnImageManifests", img_base["Id"], get=True) if False else act(M, "GetMeta_CtnImageManifests", get=True, *[{"par1": img_base["Id"]}]))
check("manifest list shows tags + robot", len(r) == 1 and sorted(r[0]["Tags"]) == ["Latest", "latest"] and r[0]["PushedBy"] == ROBOTS["platform-ci"], r)
tree = data(act(M, "GetMeta_CtnTree", get=True, *[{"par1": r_acme["Id"]}]))
check("tree has folders + image", len(tree["Folders"]) == 8 and len(tree["Images"]) == 1 and tree["Images"][0]["TagCount"] == 1, tree["Images"])
robots = data(act(M, "GetMeta_Robots", get=True))
oc = [x for x in robots if x["Name"] == ROBOTS["acme-ci"]][0]
check("robot list roots", sorted((x["ResourceId"], x["Access"]) for x in oc["Accesses"] if x["ManagerId"] == "Container") == sorted([(r_acme["Id"], "W"), (r_server["Id"], "R")]) and "TokenPrefix" in oc and "Token" not in oc, oc)

# token regenerate invalidates the old one
new = data(mg("PostGetMeta_RobotRegenerate", acme_dev[2]))
s, _, _ = http("GET", "/v2/", auth(acme_dev))
check("old token invalid after regenerate", s == 401, s)
s, _, _ = http("GET", "/v2/", auth(acme_dev, new["Token"]))
check("new token valid", s == 200, s)
# disable robot
data(mg("PostMeta_RobotUpdate", acme_dev[2], None, False))
s, _, _ = http("GET", "/v2/", auth(acme_dev, new["Token"]))
check("disabled robot -> 401", s == 401, s)
# expiry in past -> 400
check("expiry past -> 400", mg("PostGetMeta_RobotCreate", "expired", None, "2020-01-01T00:00:00Z")[0] == 400)
# delete manifest by digest / tag
s, h, b = http("DELETE", "/v2/server/base/manifests/Latest", auth(plat))
check("delete tag 202", s == 202, s)
s, h, b = http("DELETE", "/v2/server/base/manifests/" + sha(manifest), auth(plat))
check("delete manifest by digest 202", s == 202, s)
s, h, b = http("GET", "/v2/server/base/manifests/latest", auth(plat))
check("tag gone after manifest delete", s == 404, s)

# no claim -> management 401/403 without token
TOKEN_SAVE = TOKEN; TOKEN = None
s, j = mg("GetMeta_CtnRoots", get=True)
check("management without login -> 401", s == 401, s)
TOKEN = TOKEN_SAVE

# cleanup management: delete image, robots, folders, roots
check("image delete ok", mg("PostMeta_CtnImageDelete", img_api["Id"])[0] == 200)
check("robot delete ok", mg("PostMeta_RobotDelete", acme_ci[2])[0] == 200)
check("root delete with robot grants -> 409", mg("PostMeta_CtnRootDelete", r_server["Id"])[0] == 409)

# Clean up everything this script created: robots, images, folders (deepest first), then roots.
for rb in (plat, acme_dev, other): mg("PostMeta_RobotDelete", rb[2])
mg("PostMeta_CtnImageDelete", img_base["Id"])
folders = data(act(M, "GetMeta_CtnTree", get=True, *[{"par1": r_acme["Id"]}]))["Folders"]
parents = {f["Id"]: f["ParentId"] for f in folders}
def depth(i):
    d = 0
    while parents.get(i): i = parents[i]; d += 1
    return d
for f in sorted(folders, key=lambda f: -depth(f["Id"])): mg("PostMeta_CtnFolderDelete", f["Id"])
check("cleanup: roots deleted", mg("PostMeta_CtnRootDelete", r_acme["Id"])[0] == 200 and mg("PostMeta_CtnRootDelete", r_server["Id"])[0] == 200)

print("\nFAILED: %d" % len(fails), fails)
sys.exit(1 if fails else 0)
