"""End-to-end smoke test for the admin-gated user management flow.

Usage (against a running instance):

    python tests/user-management-smoke.py

Defaults to http://localhost:8081 with admin@books.local / Admin#12345 and creates
member@books.local. Override with the BASE / ADMIN_EMAIL / ADMIN_PASSWORD /
MEMBER_EMAIL / MEMBER_PASSWORD environment variables, and start the stack on a
throwaway volume so the fixtures do not collide:

    ADMIN_EMAIL=admin@books.local ADMIN_PASSWORD='Admin#12345' APP_PORT=8081 \
        docker compose -p mvcverify up -d
"""
import http.cookiejar
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("BASE", "http://localhost:8081")
ADMIN = (os.environ.get("ADMIN_EMAIL", "admin@books.local"),
         os.environ.get("ADMIN_PASSWORD", "Admin#12345"))
MEMBER_EMAIL = os.environ.get("MEMBER_EMAIL", "member@books.local")
MEMBER_PASSWORD = os.environ.get("MEMBER_PASSWORD", "Member#12345")

results = []


def check(name, ok, detail=""):
    results.append((ok, name, detail))
    print(("PASS  " if ok else "FAIL  ") + name + (("  -> " + detail) if detail else ""))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Session:
    """One browser: shared cookie jar, plus follow / no-follow openers."""

    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        cp = urllib.request.HTTPCookieProcessor(self.jar)
        self.op_follow = urllib.request.build_opener(cp)
        self.op_noredirect = urllib.request.build_opener(cp, NoRedirect())

    def fetch(self, path, data=None, follow=True):
        """Return (status, final_url, body, location)."""
        url = BASE + path
        body = urllib.parse.urlencode(data).encode() if data else None
        req = urllib.request.Request(url, data=body)
        req.add_header("User-Agent", "verify-script")
        if body:
            req.add_header("Content-Type", "application/x-www-form-urlencoded")
        op = self.op_follow if follow else self.op_noredirect
        try:
            with op.open(req) as resp:
                return resp.status, resp.geturl(), resp.read().decode("utf-8", "replace"), resp.headers.get("Location")
        except urllib.error.HTTPError as err:
            return err.code, url, err.read().decode("utf-8", "replace"), err.headers.get("Location")


TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
TBODY_RE = re.compile(r"<tbody>(.*?)</tbody>", re.S)


def token(html):
    m = TOKEN_RE.search(html)
    return m.group(1) if m else None


def user_rows(html):
    """Body of the *users* table (the page also holds the capability matrix)."""
    bodies = TBODY_RE.findall(html)
    return bodies[-1] if bodies else ""


def login(sess, email, password):
    _, _, html, _ = sess.fetch("/Account/Login")
    return sess.fetch("/Account/Login", {
        "Email": email,
        "Password": password,
        "RememberMe": "false",
        "__RequestVerificationToken": token(html),
    }, follow=False)


# ---------------------------------------------------------------- anonymous
anon = Session()
status, _, body, _ = anon.fetch("/")
check("anonymous GET / is 200", status == 200, f"status={status}")

status, _, _, location = anon.fetch("/User", follow=False)
check("anonymous GET /User redirects to login",
      status == 302 and "/Account/Login" in (location or ""), f"status={status} location={location}")

status, _, body, _ = anon.fetch("/Account/AccessDenied")
check("GET /Account/AccessDenied renders 200",
      status == 200 and "Access denied" in body, f"status={status}")

# -------------------------------------------------------------------- admin
admin = Session()
status, _, _, location = login(admin, *ADMIN)
check("admin login succeeds", status == 302, f"status={status} location={location}")

status, _, body, _ = admin.fetch("/User")
check("admin GET /User is 200", status == 200, f"status={status}")
check("admin sees the management header", "User management" in body)
check("admin sees stat cards", "No access" in body and "Admins" in body)
check("admin sees the capability matrix", "What each access level can do" in body)

# create a Member through the UI
status, _, html, _ = admin.fetch("/User/Create")
tok = token(html)
check("GET /User/Create is 200", status == 200 and tok is not None, f"status={status}")
check("create form offers the access level selector", 'name="Role"' in html)

status, _, _, location = admin.fetch("/User/Create", {
    "Email": MEMBER_EMAIL,
    "Password": MEMBER_PASSWORD,
    "ConfirmPassword": MEMBER_PASSWORD,
    "Role": "Member",
    "__RequestVerificationToken": tok,
}, follow=False)
check("POST /User/Create redirects to Index", status == 302 and location == "/User",
      f"status={status} location={location}")

status, _, body, _ = admin.fetch("/User")
check("new member appears in the list", MEMBER_EMAIL in body)
check("success banner is shown", "created with" in body)

# search + filter (assertions scoped to the users table, not the navbar)
status, _, body, _ = admin.fetch("/User?q=member")
rows = user_rows(body)
check("search q=member narrows to the member",
      MEMBER_EMAIL in rows and "admin@books.local" not in rows, f"rows={len(rows)} chars")
check("search keeps the term in the input", 'value="member"' in body)

status, _, body, _ = admin.fetch("/User?role=Admin")
rows = user_rows(body)
check("filter role=Admin hides the member",
      "admin@books.local" in rows and MEMBER_EMAIL not in rows)

status, _, body, _ = admin.fetch("/User?role=Member")
rows = user_rows(body)
check("filter role=Member shows only the member",
      MEMBER_EMAIL in rows and "admin@books.local" not in rows)

status, _, body, _ = admin.fetch("/User?role=None")
rows = user_rows(body)
check("filter role=None is empty", MEMBER_EMAIL not in rows and "admin@books.local" not in rows)
check("filter role=None shows the empty state", "No users match this filter" in rows)

status, _, body, _ = admin.fetch("/User?q=member&role=Admin")
rows = user_rows(body)
check("combined search+filter yields nothing", MEMBER_EMAIL not in rows and "No users match this filter" in rows)

# the filter form keeps the chosen access level selected
status, _, body, _ = admin.fetch("/User?role=Admin")
check("filter form re-selects the chosen level",
      re.search(r'<option value="Admin" selected="selected"', body) is not None)

# ------------------------------------------------------------------- member
member = Session()
status, _, _, location = login(member, MEMBER_EMAIL, MEMBER_PASSWORD)
check("member login succeeds", status == 302, f"status={status} location={location}")

status, _, _, location = member.fetch("/User", follow=False)
check("member GET /User is redirected to AccessDenied",
      status == 302 and "/Account/AccessDenied" in (location or ""),
      f"status={status} location={location}")

status, _, body, _ = member.fetch("/Account/AccessDenied?ReturnUrl=%2FUser")
check("member sees the access denied page", status == 200 and "Access denied" in body, f"status={status}")
check("access denied page names the required level", "Admin" in body)
check("access denied page shows the signed-in account", MEMBER_EMAIL in body)
check("access denied page echoes the requested page", "/User" in body)

status, _, body, _ = member.fetch("/Book")
check("member can still browse books", status == 200, f"status={status}")

status, _, _, location = member.fetch("/AuditLog", follow=False)
check("member is blocked from the audit log too",
      status == 302 and "/Account/AccessDenied" in (location or ""), f"location={location}")

# --------------------------------------------------------------- navigation
status, _, body, _ = member.fetch("/")
check("member navbar hides the Users link", "/User" not in body)
status, _, body, _ = admin.fetch("/")
check("admin navbar shows the Users link", "/User" in body)

# ------------------------------------------------------------- guard rails
def id_for(html, email):
    for row in re.split(r"<tr>", html):
        if email in row:
            m = re.search(r"/User/Details/([0-9a-f-]{36})", row)
            if m:
                return m.group(1)
    return None


status, _, body, _ = admin.fetch("/User")
admin_id = id_for(body, ADMIN[0])
member_id = id_for(body, MEMBER_EMAIL)
check("admin row is identifiable", admin_id is not None)
check("member row is identifiable", member_id is not None)

_, _, html, _ = admin.fetch("/User")
tok = token(html)

status, _, _, location = admin.fetch("/User/ToggleLockout", {
    "id": admin_id, "__RequestVerificationToken": tok}, follow=False)
check("admin cannot lock their own account", status == 302, f"status={status}")
_, _, body, _ = admin.fetch("/User")
check("self-lock is refused with a message", "cannot lock your own account" in body)

status, _, _, location = admin.fetch("/User/ToggleRole", {
    "id": admin_id, "role": "Admin", "__RequestVerificationToken": tok}, follow=False)
check("admin cannot drop their own Admin role", status == 302, f"status={status}")
_, _, body, _ = admin.fetch("/User")
check("self-demotion is refused with a message", "cannot remove the Admin role from your own account" in body)

status, _, _, _ = admin.fetch("/User/ToggleRole", {
    "id": member_id, "role": "Superuser", "__RequestVerificationToken": tok}, follow=False)
check("an unknown role is rejected with 400", status == 400, f"status={status}")

status, _, _, location = admin.fetch("/User/Delete", {
    "id": admin_id, "__RequestVerificationToken": tok}, follow=False)
check("admin cannot delete their own account", status == 302, f"status={status}")
_, _, body, _ = admin.fetch("/User")
check("self-delete is refused with a message", "cannot delete your own account" in body)

# lock the member: login must stop working
_, _, html, _ = admin.fetch("/User")
tok = token(html)
status, _, _, _ = admin.fetch("/User/ToggleLockout", {
    "id": member_id, "__RequestVerificationToken": tok}, follow=False)
check("admin can lock another user", status == 302, f"status={status}")
_, _, body, _ = admin.fetch("/User?q=member")
check("locked member shows the Locked badge", "Locked" in user_rows(body))

locked = Session()
status, _, _, location = login(locked, MEMBER_EMAIL, MEMBER_PASSWORD)
check("a locked member cannot sign in", status == 200 and not location,
      f"status={status} location={location}")

# unlock restores access
_, _, html, _ = admin.fetch("/User")
tok = token(html)
admin.fetch("/User/ToggleLockout", {"id": member_id, "__RequestVerificationToken": tok}, follow=False)
unlocked = Session()
status, _, _, location = login(unlocked, MEMBER_EMAIL, MEMBER_PASSWORD)
check("unlocking restores sign-in", status == 302, f"status={status} location={location}")

# grant Admin, verify the new admin can reach /User, then revert
_, _, html, _ = admin.fetch("/User")
tok = token(html)
admin.fetch("/User/ToggleRole", {"id": member_id, "role": "Admin", "__RequestVerificationToken": tok}, follow=False)
promoted = Session()
login(promoted, MEMBER_EMAIL, MEMBER_PASSWORD)
status, _, body, _ = promoted.fetch("/User")
check("a promoted member can now manage users", status == 200 and "User management" in body, f"status={status}")

_, _, html, _ = admin.fetch("/User")
tok = token(html)
admin.fetch("/User/ToggleRole", {"id": member_id, "role": "Admin", "__RequestVerificationToken": tok}, follow=False)
# Roles live in the auth cookie, so sign in again to pick the change up.
demoted = Session()
login(demoted, MEMBER_EMAIL, MEMBER_PASSWORD)
status, _, _, location = demoted.fetch("/User", follow=False)
check("demoting takes the access away again",
      status == 302 and "/Account/AccessDenied" in (location or ""), f"location={location}")

# audit trail
status, _, body, _ = admin.fetch("/AuditLog")
check("user changes are written to the audit log", "Created user" in body, f"status={status}")

# ------------------------------------------------------------------- report
failed = [r for r in results if not r[0]]
print("\n%d checks, %d failed" % (len(results), len(failed)))
sys.exit(1 if failed else 0)
