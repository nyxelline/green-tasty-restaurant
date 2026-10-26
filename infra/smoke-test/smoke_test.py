"""
API smoke test for the deployed restaurant backend. Standard library only.

Walks the whole scenario: sign in (owner, waiter), read menu/locations/tables,
book a table for 3 guests, get invite, two guests join, pick dishes, submit,
owner opens the group order, removes a dish and a guest, checks roles/401s,
cancels the reservation. Prints a table "route - expected - got - PASS/FAIL".

Usage (any folder, python 3.9+):
    python smoke_test.py --owner-email you+owner@gmail.com --waiter-email you+waiter@gmail.com
The password is asked in the console (hidden). Test data it creates is cleaned up.
"""
import argparse
import getpass
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

BASE = "https://podlgit99l.execute-api.eu-north-1.amazonaws.com/develop"
LOCATION_ID = "11111111-1111-4111-8111-000000000001"
RESULTS = []


def call(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            status, raw = resp.status, resp.read()
    except urllib.error.HTTPError as error:
        status, raw = error.code, error.read()
    except Exception as error:  # network problems
        return 0, {"error": str(error)}
    try:
        return status, json.loads(raw) if raw else None
    except ValueError:
        return status, raw.decode(errors="replace")


def pick(obj, *names):
    """Case-insensitive lookup (backend mixes camelCase and PascalCase)."""
    if isinstance(obj, dict):
        lowered = {key.lower(): value for key, value in obj.items()}
        for name in names:
            if name.lower() in lowered:
                return lowered[name.lower()]
    return None


def check(label, method, path, expected, body=None, token=None):
    status, payload = call(method, path, body, token)
    ok = status in (expected if isinstance(expected, (list, tuple)) else [expected])
    RESULTS.append((label, f"{method} {path.split('?')[0]}", expected, status, ok))
    mark = "PASS" if ok else "FAIL"
    print(f"[{mark}] {label:<52} {method:<6} -> {status}")
    if not ok:
        print(f"        {str(payload)[:300]}")
    return status, payload


def verify(label, condition, detail=""):
    """A check on data we already have (no request)."""
    RESULTS.append((label, "(data check)", "true", "true" if condition else "false", bool(condition)))
    print(f"[{'PASS' if condition else 'FAIL'}] {label:<52} (data)")
    if not condition and detail:
        print(f"        {detail}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--owner-email", required=True)
    parser.add_argument("--waiter-email", required=True)
    args = parser.parse_args()
    password = os.environ.get("SMOKE_PASSWORD") or getpass.getpass("Password of the test users (hidden): ")

    print(f"Backend: {BASE}\n")
    # ---- public
    _, locations = check("Locations list (public)", "GET", "/locations", 200)
    check("Location by id", "GET", f"/locations/{LOCATION_ID}", 200)
    _, dishes = check("Dishes of location", "GET", f"/locations/{LOCATION_ID}/dishes", 200)
    check("Popular dishes", "GET", "/dishes/popular", 200)
    check("Protected route without token -> 401", "GET", "/reservations", [401, 403])
    check("Sign in with wrong password -> 4xx", "POST", "/signin",
          [400, 401, 403], {"email": args.owner_email, "password": "Wrong-pass-1!"})

    # ---- auth
    s, owner = check("Sign in owner", "POST", "/signin", 200,
                     {"email": args.owner_email, "password": password})
    owner_token = pick(owner, "idToken")
    s, waiter = check("Sign in waiter", "POST", "/signin", 200,
                      {"email": args.waiter_email, "password": password})
    waiter_token = pick(waiter, "idToken")
    if not owner_token or not waiter_token:
        print("\nCannot continue without tokens.")
        return finish()

    check("GET /me (owner)", "GET", "/me", 200, token=owner_token)
    check("GET /me (waiter)", "GET", "/me", 200, token=waiter_token)

    check("Owner cannot list tables (waiter only) -> 403", "GET", f"/locations/{LOCATION_ID}/tables/all", 403, token=owner_token)
    # ---- tables
    _, tables = check("Tables of location (waiter only)", "GET", f"/locations/{LOCATION_ID}/tables/all", 200, token=waiter_token)
    table_list = tables if isinstance(tables, list) else (pick(tables, "items", "tables") or [])
    def capacity(table):
        for key, value in (table or {}).items():
            if isinstance(value, int) and ("capac" in key.lower() or "seat" in key.lower()):
                return value
        return 0

    big_tables = [item for item in table_list if capacity(item) >= 3]
    table_id = pick(big_tables[0], "id", "tableId") if big_tables else None
    dish_list = dishes if isinstance(dishes, list) else []
    verify("Dishes carry allergens and diets lists",
           bool(dish_list) and all(isinstance(pick(d, "allergens"), list) and isinstance(pick(d, "diets"), list) for d in dish_list))
    risotto = next((d for d in dish_list if pick(d, "name") == "Mushroom Risotto"), None)
    if risotto:
        verify("Seeded tags: Mushroom Risotto contains dairy", "dairy" in (pick(risotto, "allergens") or []))
    available = [d for d in dish_list if str(pick(d, "state")).lower() in ("available", "1")]
    on_stop = [d for d in dish_list if str(pick(d, "state")).lower() in ("onstop", "on stop", "0")]
    if not table_id or len(available) < 2:
        print("\nNeed a table for 3+ guests and 2+ available dishes - run seed_dev_data.py first.")
        return finish()

    # ---- reservation (try several slots, tomorrow+)
    reservation_id = None
    base_day = datetime.now(timezone.utc).replace(minute=0, second=0, microsecond=0) + timedelta(days=3)
    for hour in (10, 12, 14, 16, 18, 20):
        start = base_day.replace(hour=hour)
        body = {"tableId": table_id, "startTime": start.strftime("%Y-%m-%dT%H:%M:%SZ"),
                "endTime": (start + timedelta(hours=1, minutes=30)).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "guestCount": 3}
        status, payload = call("POST", "/reservations", body, owner_token)
        if status == 201:
            reservation_id = pick(payload, "id")
            RESULTS.append(("Create reservation (3 guests)", "POST /reservations", 201, 201, True))
            print(f"[PASS] {'Create reservation (3 guests)':<52} POST   -> 201 (slot {start:%H:%M}Z)")
            break
    if not reservation_id:
        RESULTS.append(("Create reservation (3 guests)", "POST /reservations", 201, status, False))
        print(f"[FAIL] Create reservation -> {status} {str(payload)[:300]}")
        return finish()

    check("My reservations", "GET", "/reservations", 200, token=owner_token)
    check("Reservation by id", "GET", f"/reservations/{reservation_id}", 200, token=owner_token)

    # ---- group invite + guests
    _, invite = check("Invite token", "GET", f"/reservations/{reservation_id}/invite-token", 200, token=owner_token)
    invite_token = pick(invite, "token")
    _, info = check("Invite info (public)", "GET", f"/guest/invites/{invite_token}", 200)
    group_id = pick(info, "groupReservationId") or reservation_id
    check("Group status", "GET", f"/guest/groups/{group_id}/status", 200)

    guests = []
    for name in ("Smoke Guest A", "Smoke Guest B"):
        _, created = check(f"Guest joins: {name}", "POST", "/guest/sessions", [200, 201],
                           {"groupReservationId": group_id, "guestName": name})
        guests.append((pick(created, "sessionId", "id"), pick(created, "sessionToken", "token")))
    (sid_a, tok_a), (sid_b, tok_b) = guests
    if not (sid_a and tok_a and sid_b and tok_b):
        print("\nGuest sessions missing.")
        return finish()

    _, menu = check("Guest menu", "GET", f"/guest/sessions/{sid_a}/menu", 200, token=tok_a)
    menu_dishes = [d for group in (pick(menu, "dishes") or {}).values() for d in group]
    on_stop_ids = [pick(d, "id") for d in menu_dishes if pick(d, "selectable") is False]
    verify("Guest menu lists a dish that cannot be selected (On Stop)", bool(on_stop_ids))
    verify("Guest menu dishes carry allergens and diets",
           bool(menu_dishes) and all(isinstance(pick(d, "allergens"), list) for d in menu_dishes))
    check("Guest sees own session", "GET", f"/guest/sessions/{sid_a}", 200, token=tok_a)
    check("Guest A cannot read guest B session -> 401/403", "GET", f"/guest/sessions/{sid_b}", [401, 403], token=tok_a)
    dish_a, dish_b = pick(available[0], "id"), pick(available[1], "id")
    check("Guest A adds dish 1 (x2)", "POST", f"/guest/sessions/{sid_a}/dishes", 200,
          {"dishId": dish_a, "quantity": 2}, tok_a)
    check("Guest A adds dish 2", "POST", f"/guest/sessions/{sid_a}/dishes", 200,
          {"dishId": dish_b, "quantity": 1}, tok_a)
    check("Guest B adds dish 1", "POST", f"/guest/sessions/{sid_b}/dishes", 200,
          {"dishId": dish_a, "quantity": 1}, tok_b)
    if on_stop_ids:
        check("Dish 'On Stop' cannot be added -> 4xx", "POST", f"/guest/sessions/{sid_b}/dishes",
              [400, 404, 409], {"dishId": on_stop_ids[0], "quantity": 1}, tok_b)
    check("Guest A removes 1 pc of dish 2", "DELETE", f"/guest/sessions/{sid_a}/dishes", 200,
          {"dishId": dish_b, "quantity": 1}, tok_a)
    check("Guest A submits", "POST", f"/guest/sessions/{sid_a}/submit", 200, token=tok_a)
    check("Guest A cannot add after submit -> 4xx", "POST", f"/guest/sessions/{sid_a}/dishes",
          [400, 409], {"dishId": dish_a, "quantity": 1}, tok_a)
    check("Group status after submit", "GET", f"/guest/groups/{group_id}/status", 200)

    # ---- split bill
    price_a = float(pick(available[0], "price"))
    _, bill = check("OWNER: group bill (split bill)", "GET", f"/guest/groups/{group_id}/bill", 200, token=owner_token)
    bill_guests = pick(bill, "guests") or []
    subtotals = [float(pick(g, "subtotal") or 0) for g in bill_guests]
    verify("Bill: at least 2 people and total = sum of subtotals",
           len(bill_guests) >= 2 and abs(sum(subtotals) - float(pick(bill, "total") or 0)) < 0.005,
           f"guests={len(bill_guests)} subtotals={subtotals} total={pick(bill, 'total')}")
    verify("Bill: shares add up to about 100%",
           abs(sum(float(pick(g, "sharePercent") or 0) for g in bill_guests) - 100) <= 0.3)
    check("Guest cannot open the owner's group bill -> 401/403", "GET", f"/guest/groups/{group_id}/bill", [401, 403], token=tok_a)
    check("Anonymous cannot open the group bill -> 401/403", "GET", f"/guest/groups/{group_id}/bill", [401, 403])
    _, mine = check("Guest A: own bill (after submit)", "GET", f"/guest/sessions/{sid_a}/bill", 200, token=tok_a)
    verify("Guest A bill = 2 x dish 1", abs(float(pick(mine, "subtotal") or 0) - 2 * price_a) < 0.005,
           f"subtotal={pick(mine, 'subtotal')} expected={2 * price_a}")
    check("Guest A cannot read guest B's bill -> 401/403", "GET", f"/guest/sessions/{sid_b}/bill", [401, 403], token=tok_a)

    # ---- owner group order (my new feature)
    _, group = check("OWNER: group sessions (new route)", "GET", f"/guest/groups/{group_id}/sessions", 200, token=owner_token)
    check("Guest cannot open owner group view -> 401/403", "GET", f"/guest/groups/{group_id}/sessions", [401, 403], token=tok_a)
    check("Waiter-less anonymous group view -> 401/403", "GET", f"/guest/groups/{group_id}/sessions", [401, 403])
    check("OWNER removes dish from guest B (new route)", "DELETE",
          f"/guest/groups/{group_id}/sessions/{sid_b}/dishes/{dish_a}", 200, token=owner_token)
    check("OWNER removes unknown dish -> 404", "DELETE",
          f"/guest/groups/{group_id}/sessions/{sid_b}/dishes/no-such-dish", [404, 400], token=owner_token)
    check("Guest cannot use owner remove-dish -> 401/403", "DELETE",
          f"/guest/groups/{group_id}/sessions/{sid_b}/dishes/{dish_a}", [401, 403], token=tok_b)
    check("OWNER removes guest B", "DELETE", f"/guest/groups/{group_id}/sessions/{sid_b}", 200, token=owner_token)
    check("Waiter view of reservation", "GET", f"/reservations/{reservation_id}", [200, 403], token=waiter_token)
    check("Orders list (owner)", "GET", "/orders", 200, token=owner_token)
    check("Notifications (waiter)", "GET", "/notifications", 200, token=waiter_token)

    # ---- cleanup
    check("Cancel reservation (cleanup)", "DELETE", "/reservations", 200, {"id": reservation_id}, owner_token)
    return finish()


def finish():
    failed = [row for row in RESULTS if not row[4]]
    print("\n" + "=" * 78)
    print(f"{'CHECK':<52} {'EXPECTED':<10} {'GOT':<5} RESULT")
    for label, route, expected, got, ok in RESULTS:
        print(f"{label[:51]:<52} {str(expected):<10} {got!s:<5} {'PASS' if ok else 'FAIL'}")
    print("=" * 78)
    print(f"TOTAL {len(RESULTS)}   PASSED {len(RESULTS) - len(failed)}   FAILED {len(failed)}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
