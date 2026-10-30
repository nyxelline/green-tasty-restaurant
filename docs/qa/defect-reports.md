# Defect Reports – Green & Tasty

Format follows the course template (ID/Summary/Env-Priority-Severity, Pre-conditions, Steps/Actual/Expected).
All defects below were found during manual testing of the deployed build and have since been fixed;
`Status` reflects the state at the time of writing.

## Known Issues (not fixed)

- **Possible race condition in table availability search** – `TableService.cs` (`GetAvailableTablesAsync`)
  collects results from concurrent per-table checks into a shared, non-thread-safe `List<Table>`.
  Under concurrent load this could intermittently drop a genuinely free table from the results with
  no error shown. Found via code review only; not confirmed live, needs a load test to reproduce.

---

**ID: DR-01** – Submitted pre-order dish shows "(Unknown)" status instead of its real state
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Customer has a submitted pre-order with at least one dish.

**Steps:**
1. Open "My Pre-Order" after submitting.
2. Look at the status badge on any dish line.

**Actual result:** Badge shows "(Unknown)" for every dish.
**Expected result:** Badge shows the dish's real state (Pending / Confirmed / Rejected).

**Root cause:** The backend response model (`OrderItemResponse`) had no `state` field at all – the frontend always received `undefined`.

---

**ID: DR-02** – "Added to X's table" message shows the guest's own name instead of the owner's
**Env:** P_PRD | **Priority:** Medium | **Severity:** Minor | **Status:** Fixed

**Pre-conditions:** Guest has joined a group session and submitted dishes.

**Steps:**
1. As a guest named e.g. "Alex", submit the cart.
2. Read the confirmation message.

**Actual result:** "Your selections have been added to Alex's table" (guest's own name).
**Expected result:** "...added to [owner's name]'s table" – it's the owner's table, not the guest's.

---

**ID: DR-03** – Owner cannot pre-order immediately after creating a reservation
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Customer has just booked a table in the current session.

**Steps:**
1. From the booking-confirmation modal, click "Make Pre-Order".
2. Observe the Menu page.

**Actual result:** Menu page opens with no reservation context – pre-order cannot be started. Closing and reopening via "My Reservations" works fine.
**Expected result:** Pre-order starts immediately, same as via "My Reservations".

**Root cause:** The confirmation modal navigated to `/menu` without the `reservationId`/`reservationLabel` query params the Menu page needs.

---

**ID: DR-04** – Two guests can join a group reservation with the exact same name, including the owner's name
**Env:** P_PRD | **Priority:** Medium | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation is `Open`.

**Steps:**
1. Join the group twice with the same guest name (or with the owner's real name).

**Actual result:** Two indistinguishable participant rows; a guest using the owner's own name is indistinguishable from the owner in the bill/participant list.
**Expected result:** A repeated name is automatically suffixed (`Name (2)`, `Name (3)`, ...), including when it collides with the owner's display name.

---

**ID: DR-05** – New guest cannot join after the owner raises guest count on an already-submitted group
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation status is `Submitted`.

**Steps:**
1. Owner increases the reservation's guest count.
2. Owner tries to fetch the invite link / a new guest tries to join.

**Actual result:** Invite link fetch and join both fail – group reservation stays locked as `Submitted`.
**Expected result:** Increasing guest count reopens the group (`Open`) so new guests can join.

---

**ID: DR-06** – Cancelled pre-order dishes silently disappear from the guest's own view
**Env:** P_PRD | **Priority:** Medium | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Owner has removed/rejected a dish from a guest's submitted cart.

**Steps:**
1. As the guest, reload the pre-order/cart view.

**Actual result:** The rejected dish is filtered out entirely – the guest has no way to see what was removed.
**Expected result:** The dish stays visible, flagged as "Rejected", so the guest knows what changed and why their total is lower.

---

**ID: DR-07** – Waiter could update the status of reservations not assigned to them
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Two waiters exist; a reservation is assigned to waiter A only.

**Steps:**
1. As waiter B, call the update-status endpoint for that reservation.

**Actual result:** Request succeeds – any waiter could change the status of any table, not just their own.
**Expected result:** Only the assigned waiter can update the reservation's status; others get 403.

---

**ID: DR-08** – Waiter's reservation list fails entirely if a single reservation record can't be converted
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Waiter is authenticated; at least one reservation assigned to them has data that fails during response conversion (e.g. a dangling `TableId` whose table/location no longer exists, or an order lookup failure).

**Steps:**
1. Sign in as a waiter whose assigned reservations include at least one such "bad" record.
2. Open the waiter dashboard ("Reservations").

**Actual result:** `GET /reservations` returns `500 {"error":"Internal Server Error: Error while fetching reservation details."}`. The waiter sees zero reservations, including healthy ones.
**Expected result:** One bad reservation shouldn't take down the whole list, same as the customer-facing path (which skips bad records instead of failing entirely).

**Root cause:** `GetReservationsForUserHandler.cs` → `GetReservationsForWaiterAsync` aborts the whole request with a 500 the moment any single reservation fails conversion, instead of skipping it like the customer path does.
