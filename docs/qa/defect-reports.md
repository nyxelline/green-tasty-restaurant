# Defect Reports – Green & Tasty

Format follows the course template (ID/Summary/Env-Priority-Severity, Pre-conditions, Steps/Actual/Expected).
All defects below were found during manual and code-level testing of the deployed build and have since
been fixed; `Status` reflects the state at the time of writing. This is a curated set of the 7 most
representative defects found across the project, chosen to show breadth (authorization, silent data
corruption, infra misconfiguration, group/guest consistency, resilience, concurrency).

## Known Issues (not fixed)

- **Possible race condition in table availability search** – `TableService.cs` (`GetAvailableTablesAsync`)
  collects results from concurrent per-table checks into a shared, non-thread-safe `List<Table>`.
  Under concurrent load this could intermittently drop a genuinely free table from the results with
  no error shown. Found via code review only; not confirmed live, needs a load test to reproduce.
- **Spurious "unexpected error" toast after successful actions under list-load burst** – the same
  unbounded concurrent fan-out pattern fixed in DR-07 (waiter dashboard) also exists on the
  customer-facing `/reservations` page; it self-recovers after ~5s, but a background burst of failed
  enrichment calls in flight can surface a misleading generic error toast even when the action the
  customer just took (e.g. creating or submitting something) actually succeeded.

---

**ID: DR-01** – Waiter could update the status of reservations not assigned to them
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Two waiters exist; a reservation is assigned to waiter A only.

**Steps:**
1. As waiter B, call the update-status endpoint for that reservation.

**Actual result:** Request succeeds – any waiter could change the status of any table, not just their own.
**Expected result:** Only the assigned waiter can update the reservation's status; others get 403.

---

**ID: DR-02** – Waiter's reservation list fails entirely if a single reservation record can't be converted
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Waiter is authenticated; at least one reservation assigned to them has data that fails during response conversion (e.g. a dangling `TableId` whose table/location no longer exists, or an order lookup failure).

**Steps:**
1. Sign in as a waiter whose assigned reservations include at least one such "bad" record.
2. Open the waiter dashboard ("Reservations").

**Actual result:** `GET /reservations` returns `500 {"error":"Internal Server Error: Error while fetching reservation details."}`. The waiter sees zero reservations, including healthy ones.
**Expected result:** One bad reservation shouldn't take down the whole list, same as the customer-facing path (which skips bad records instead of failing entirely).

**Root cause:** `GetReservationsForUserHandler.cs` → `GetReservationsForWaiterAsync` aborts the whole request with a 500 the moment any single reservation fails conversion, instead of skipping it like the customer path does.

---

**ID: DR-03** – New guest cannot join after the owner raises guest count on an already-submitted group
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation status is `Submitted`.

**Steps:**
1. Owner increases the reservation's guest count.
2. Owner tries to fetch the invite link / a new guest tries to join.

**Actual result:** Invite link fetch and join both fail – group reservation stays locked as `Submitted`.
**Expected result:** Increasing guest count reopens the group (`Open`) so new guests can join.

---

**ID: DR-04** – Group pre-order submit fails with "Order not found" when the owner never built their own cart
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation has at least one guest with a submitted pre-order; the owner never added dishes to their own cart.

**Steps:**
1. As the owner, open the group's consolidated view and click "Submit Group Order" without ever opening the menu themselves.

**Actual result:** `POST /group-reservations/{id}/submit` returns `400 "Order not found."` – the whole group submit is blocked, even though only the owner (not guests) can perform it.
**Expected result:** The group submits successfully, merging all guests' dishes; the owner isn't required to have pre-ordered anything themselves.

**Root cause:** `GuestSessionService.SubmitGroupPreOrderAsync` looked up the owner's existing order and threw if none existed, instead of creating an empty draft order to merge guest dishes into. Fixed by auto-creating that draft order (`CreateEmptyOwnerOrderAsync`) when missing.

---

**ID: DR-05** – Editing a reservation's time can silently save a different month than the one selected
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Today's date is near month-end with more days than the target month has (e.g. editing on Oct 31 to a date in November).

**Steps:**
1. Open "Edit Reservation" on Oct 31 and change the time to Nov 3, 11:00 AM.
2. Save and reopen the reservation.

**Actual result:** The reservation silently saves as Dec 3 instead of Nov 3 – no error, no warning.
**Expected result:** The reservation saves on the exact date selected.

**Root cause:** `date-helpers.ts` → `revertFormatTime` built the target date by calling `setFullYear`/`setMonth`/`setDate` sequentially on `new Date()` (today). Setting the month first while the day-of-month (31) doesn't exist in the target month (November has 30 days) rolls the date over into December before `setDate` runs. This function is also used by table/time-slot search, so the bug's blast radius wasn't limited to the edit modal. Fixed by constructing the target date with a single `Date(year, month, day, h, m)` constructor call instead of sequential setters.

---

**ID: DR-06** – Logged-in customer gets "Unauthorized", then "Forbidden", when submitting feedback – two stacked bugs
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed (both confirmed live)

**Pre-conditions:** Customer has a Finished reservation with an order attached (feedback is eligible).

**Steps:**
1. As the customer, open "Leave Feedback" on a Finished reservation, rate the service, and submit.

**Bug 1 – Actual result:** `POST /feedback` returned `401 "User is not authenticated."` and the UI showed "Please log in to perform the action!" – with the same valid, non-expired token that successfully authorizes `GET /feedback`, `GET /orders/{id}`, etc. on the same request.
**Root cause:** Infra-level misconfiguration, not application code: in `deployment_resources.json`, the `/feedback` resource's `POST` method was deployed with `"authorization_type": "NONE"` (its sibling `PUT` method correctly used `"cognito_authorizer"`), so API Gateway never attached the Cognito authorizer to `POST /feedback` and no claims reached the Lambda. Fixed by changing `POST /feedback`'s `authorization_type` to `cognito_authorizer`. **Confirmed live** after redeploy: the 401 is gone.

**Bug 2 – Actual result:** With the 401 gone, the same request now returns `403 "Only customers can create feedback."` for an actual customer account, consistently (3/3 attempts), confirmed live via direct API call with a valid customer session token.
**Root cause:** `UserService.GetUserRoleAsync` tried to read a `"cognito:groups"` value off `user.Attributes` (Cognito's AdminGetUser/ListUsers attribute list) – but `cognito:groups` is a JWT claim added by the authorizer at token-issue time, never a persisted Cognito user attribute, so that lookup always returned empty and the method always returned `null`, tripping the `userRole != Roles.Customer` check for every user regardless of their real role. `GetAuthorizedUserHandler` already had the correct pattern (`AdminListGroupsForUser`) for the same problem. Fixed by adding `IUserRepository.GetUserGroupsAsync` (via `AdminListGroupsForUser`) and rewriting `GetUserRoleAsync` to use it. **Confirmed live** after redeploy: `POST /feedback` now returns `200` with the created feedback record.

**Expected result:** A logged-in customer with a valid session can submit feedback end-to-end.

---

**ID: DR-07** – Waiter dashboard shows "Unknown customer" and "0 dishes" for most reservations under normal load
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Waiter's reservation list has more than ~6 entries requiring per-row customer-name and order enrichment.

**Steps:**
1. Sign in as a waiter with a realistic number of reservations (20+).
2. Open the dashboard and watch the list populate.

**Actual result:** A large fraction of rows show "Unknown customer" / "0 dishes"; browser console shows `net::ERR_FAILED` / CORS-masked `status: 0` errors on `/orders/{id}` and `/users/{id}/name`. Root cause: all enrichment calls fired at once (`forkJoin`) exceed the backend's effective concurrency limit, and a throttled response arrives without CORS headers, which the browser reports as a generic CORS failure instead of the real throttling error.
**Expected result:** Every reservation resolves its customer name and order summary regardless of list size.

**Root cause / fix:** Unbounded concurrent fan-out in `waiter-dashboard.component.ts`. Replaced `forkJoin` with `mergeMap(..., 2)` (concurrency capped at 2, tuned empirically against the live API) to stay under the backend's throttle limit.
