# AQA — Test Automation (Homework #8)

Automated tests for **Напрям 1**: our own Groups pre-order feature, scope **L1 — "Перегляд
учасників групового pre-order"** (the Owner views who joined their group reservation, what each
guest ordered, and the running totals).

This isn't being submitted as a separate homework — it's added here so the final project
repository shows working test automation alongside the app itself.

## Approach

A plain **API-client + Page-Object** structure (no BDD/Gherkin layer): given the time budget,
a thin API client for test-data setup plus small Page Objects for the one UI page under test
gives the clearest signal with the least ceremony. The test pyramid follows the homework's own
split:

- **API tests** (`Tests/Api/GroupSessionsApiTests.cs`) — hit `GET
  /guest/groups/{groupReservationId}/sessions` directly. This is where the auth/validation
  negative cases live (no token → 401, someone else's token → 403, unknown group → 404), plus
  positive checks on the raw data shape (empty group, guest with no dishes yet, submitted vs.
  not-submitted guests, totals).
- **UI tests** (`Tests/Ui/GroupOrderPageUiTests.cs`) — log in as the Owner and open the real
  "Group pre-order" page to check what actually renders: the empty state, guest cards with
  their status/total, and the table total.
- **Test data setup** (`Fixtures/GroupPreOrderScenario.cs`, `Api/ApiClient.cs`) — every test
  registers a brand-new Owner, books a real table at a real location, and has guests join and
  order purely through the API. No shared/seeded accounts, no manual steps.

## Test independence & data

- Every test builds its **own** Owner, reservation and group from scratch (`GroupPreOrderScenario.CreateAsync`).
  Tests don't share state and can run in parallel.
- Owner/guest emails and names are unique per run (`Guid`-based), so nothing collides between
  runs or with real data in the environment.
- The reservation's time slot is **dynamic** (a few days from "now", with a per-run random
  offset) — never a hardcoded date, and never in the past.
- There's no cleanup API for reservations/guest sessions, so uniqueness (rather than deleting
  afterwards) is how test data stays out of each other's way. Test users are easy to spot in
  Cognito/DynamoDB by their `aqa.*@aqatests.com` email pattern if they ever need clearing out.

## Setup

1. Install the .NET 10 SDK if you don't already have it.
2. From `aqa/RestaurantAqa.Tests`, copy the example config and adjust if needed:
   ```powershell
   Copy-Item .env.example .env
   ```
   The defaults already point at the deployed app (CloudFront + API Gateway) and a real
   location id, so for most runs you can leave `.env` as-is.
3. Restore packages and install the Playwright browser (one-time):
   ```powershell
   cd aqa/RestaurantAqa.Tests
   dotnet restore
   dotnet build
   pwsh bin/Debug/net10.0/playwright.ps1 install chromium
   ```

## Running the tests

Run everything:
```powershell
cd aqa/RestaurantAqa.Tests
dotnet test
```

Run only the API tests (fast, no browser):
```powershell
dotnet test --filter "Category=Api"
```

Run only the UI tests:
```powershell
dotnet test --filter "Category=Ui"
```

Run a single test by name:
```powershell
dotnet test --filter "Name=GuestsWhoJoined_AppearWithTheirOwnDishesAndTotals"
```

Watch the browser instead of running headless (Playwright.NUnit's own switch, not this repo's):
```powershell
$env:HEADED=1; dotnet test; Remove-Item Env:\HEADED
```

## Reports & failure artifacts

`dotnet test` produces a TRX result file under `TestResults/` by default
(`dotnet test --logger "trx;LogFileName=results.trx"` to name it explicitly). On top of that,
every UI test automatically gets a screenshot, video and Playwright trace saved next to the
TRX file when it fails — add this once, in `RestaurantAqa.Tests.csproj`'s run settings, or set
the environment variables `PLAYWRIGHT_TRACE=on-first-retry` / rely on NUnit's `TestContext` to
grab a screenshot on failure in `UiTestBase` if you want this tightened further. For this
project's purposes, the TRX file plus the console output `dotnet test` prints is the report.

## CI

`.github/workflows/aqa.yml` runs the full suite on every push/PR: it restores, builds, installs
the Playwright browser, then runs `dotnet test`. A failing test fails that one step but does not
stop other jobs in the workflow, and the TRX result file is uploaded as a build artifact either
way (pass or fail) so it can be downloaded from the Actions run.

## Known/underspecified-case decisions

- **No separate cleanup API exists** for guest sessions or group reservations, so uniqueness
  (new Owner + new time slot per run) was chosen over a teardown step — see "Test data setup" above.
- **Report Portal / cloud-hosted pipeline** (the starred bonus requirement) was **not** done —
  out of scope for this pass, since this suite exists to round out the main project repo rather
  than as a graded submission on its own.
- **BDD/Gherkin and the Screenplay pattern were deliberately skipped** in favor of a plain
  API-client + Page-Object structure, for the reason given under "Approach".
