# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code.

## What MatDo is

A SaaS task manager with the features of Todoist plus teams. The heart is the **quick add**: one line of German or English text is read into a task (date, time,
repeating, `#project`, `/section`, `@label`, `p1..p4`, `{deadline}` and the **keywords** each person defines, e.g. "Projekt EIMER"). The same parser serves the
editor (live, marks in the text) and the **webhook** (`POST /api/v1/webhook/{token}`, for Apple Shortcuts dictation). Teams share projects; tasks of team projects can be assigned.
UI languages: **English and German** (catalog, see below). Port **6006**. Accent **#d03d33** (Todoist-like red, AA with white).

Stack: .NET 10, ASP.NET Core **Razor Pages**, EF Core + **PostgreSQL**, the **MatUi** toolkit of the sister project MatDesign (copied into `wwwroot/matui`, never edited by hand;
copied from MatDesign itself: `http://localhost:8060/ui/matui.css`, `matui.js`, `matui.boot.js`, `matui-icons.svg` of the running MatDesign, the bundle of `../MatDesign/src/MatUi` – never from a sister app, their copies are older; version and build in the first line of `matui.css`). Follow the rules of MatDesign (`../MatDesign/src/MatDesign.Core/RuleBook.cs`), e.g. LIST-11: an empty list offers its first item in `mat-empty--first` (partial `_Empty`).

## Build / run / test

- `dotnet build MatDo.slnx` (note `.slnx`), `dotnet test` (xunit: `QuickAddParserTests`, `RecurrenceTests`).
- **Run / test the app: rebuild the container** – `./scripts/redeploy.ps1` (`-Clean` removes the volumes). Stack `docker-compose.local.yml`: web 6006, pgAdmin 6007, Postgres 6008.
  The local network has a fixed subnet (10.253.66.0/24): this PC runs out of Docker address pools.
- Compose files: `docker-compose.yml` (main, `:latest`), `docker-compose.dev.yml` (dev, `:nightly`, pgAdmin), `docker-compose.local.yml` (build). **No `${}` placeholders, no environment
  for the web service** (defaults are in `appsettings.json` / the Dockerfile), stack name sets the prefix, so services are `web`/`db`/`pgadmin` and volumes `web-data`/`db-data`/`pgadmin-data`.
- E2E: Playwright (`playwright-core`, installed Edge, see the `screenshots` skill). Screenshots in `docs/images/`. Check 390 px and dark mode for every new page.
- Migrations: `dotnet ef migrations add <Name> -p src/MatDo -o Data/Migrations` (design-time factory exists); applied at start-up.
- Git Bash `curl -d` with umlauts sends Latin-1: test the webhook with `--data-binary @file` (UTF-8).

## Map

```
src/MatDo
  Program.cs                    composition root (cookie auth with DB sessions, razor conventions, rate limits "webhook"/"signin", /health, APIs)
  Infrastructure.cs             HealthCheck (--healthcheck), Database.MigrateWithRetry, SecurityHeaders (CSP: no inline script/style), MaintenanceService, EmptyStringBinderProvider
  Data/Entities.cs, AppDbContext.cs   PascalCase singular tables, Id BIGINT, audit columns, UpdateState soft delete (SaveChanges), Token (uuid) on UserSession and WebhookToken
  Services/Parsing/QuickAddParser.cs  THE parser (pure): candidates per kind, accepted left to right (longer first, one per kind, labels many); the rest is the title
  Services/Recurrence.cs        rules d:n, wd, w:n[:dow], m:n[:day], y:n; Next() for checking off a repeating task
  Services/QuickAddService.cs   ParseContext of a person (projects, labels, sections, keywords) + CreateAsync (editor and webhook); manual choices win over the text, Default* lose
  Services/ProjectService.cs    visibility: personal (owner) or team (members) – every task query starts with Visible()
  Services/TaskService.cs       rows of lists (TaskRow), complete/reopen/delete/reorder, labels
  Localization/                 TextCatalog + CatalogLocalizer (L, Text.T), Languages (en/de, provider, cookie), ClientTexts (html attributes, Js.* block)
  Resources/Texts/*.json        the texts, EN and DE
  Services/TeamService.cs, MenuService.cs (menus of the switcher), Dates.cs (German formatting, user time zone), Ui.cs, TaskTools.cs (toolbar of task lists), QuickAddEditor.cs (editors of a page)
  Api/WebhookApi.cs             POST /api/v1/webhook/{token} (+ Bearer variant): JSON / form / text → task, 201 JSON with message
  Api/AppApi.cs                 /api/app/parse (live parse), tasks/{id}/complete|reopen, tasks/reorder (cookie + RequestVerificationToken header)
  Controls/                     ONE code base per control: <md-field>, <md-buttons>, <md-table>, <md-tabs>; partials _Toolbar, _Pager, _FormSummary, _TaskList, _QuickAdd
  Pages/                        Today, Upcoming, Inbox (→ project), Projects/, Sections/, Tasks/ (Add = target of every editor, Edit, Index = filter, Completed), Labels/, Teams/,
                                Account/ (profile, Keywords/, Webhooks/, Password, Login, Register, Logout), Admin/ (Users/, System)
  wwwroot/js/quickadd.js        the editor: mirror with <mark>s behind a transparent textarea, chips, pops, Enter/Esc, Q opens the dialog
  wwwroot/js/tasks.js           checks in the background, mat:reorder → order
  wwwroot/css/app.css           accent + md- classes only, tokens of the toolkit
  wwwroot/robots.txt, llms.txt, manifest.webmanifest, favicon.svg, img/logo/ (logo in detail levels)
```

## Conventions

- **Languages (EN / DE) – every text**: `Resources/Texts/*.json`, one key `Area.Thing` → `{"en", "de"}` (MatBill pattern). Views: `@L["Key"]` (injected in `_ViewImports`), code: `Text.T("Key", args)` (`MatDo.Localization`), scripts: keys `Js.*` come as a JSON block (`ClientTexts.Script()`, read with `t(...)`). Language: account (`User.Language`, empty = browser) → cookie `.MatDo.Language` (switch on the pages without a menu, POST `/language`) → Accept-Language → en. `Dates` writes names in the current language. The webhook answers in the language of the token owner. `TextCatalogTests` fails for a missing key, a missing language or different placeholders. No literal UI text in code.
- **Only toolkit controls** (FORM-12, FORM-15): fields go through `<md-field>`; a date is `kind="date"`, a time `kind="time"` (MatUi pickers, posting 2026-10-10 / 09:30), a choice from a list `kind="select"`/`"picker"` (MatUi select picker), up to five exclusive choices `kind="radios"`. No native `type=date|time` and no native `<select>` anywhere (also not in partials like `_QuickAdd`/`_Toolbar`: write the `mat-picker` markup; set a picker from code with `MatUi.picker.set`, after `form.reset()` call `MatUi.picker.init(scope)`). Check new fields against the RuleBook before building them.
- **The task controls are one code base**: the date (day, time, repeat, date in words) is the panel `_DuePop` + `MatDoPick.due` (pick.js), the priority `_PriorityPop` + `MatDoPick.priority`; the quick add, the page of a task (date, deadline, priority as fields that open the panel, `taskedit.js`) and any new place use them – never separate date/time/repeat fields. Filters with up to four values are `mat-options` in the filter dialog, more are a select picker; a view option such as "show completed" is a filter there, not a button of its own.
- **Few words**: no key hints (`<kbd>`) on the pages and no subtitle or field hint that only repeats the label; every key of the app is listed on `/Account/Hotkeys` (settings menu) – a new key goes there. A subtitle carries information (a date, a count), not an explanation.
- **Phone app first**: task views put search / filter / sort as icon buttons into the top bar (`ToolbarModel.InTopBar`, partial `_ToolbarTop` in `@section TopActions`, at most four with the + of the quick add); the chips stay above the list. Rows: swipe right = complete, left = tomorrow / delete (MatUi `mat-item--sliding`, `mat:item-action`), long press / right click / menu key = `_TaskMenu` (popover, a sheet on phones; APIs `/api/app/tasks/{id}/due|priority|delete`). Buttons that are not the main one carry `md-btn-collapse` (icon only below 600 px, label as aria-label/tooltip). Popovers (`popover="manual"`) for panels and menus so a dialog never clips them; reset the UA `width: fit-content` on phones.
- **PWA / offline (small on purpose)**: `wwwroot/sw.js` keeps only the static files and `offline.html`; pages always come from the network, without one the offline page. There new tasks are noted (and in the quick add while offline): `js/offline.js` keeps them on the device (localStorage `md-notes`, per account) and sends them when the network is back to `POST /api/app/capture`, which goes the way of the webhook (`WebhookApi.FromTextAsync`: keywords, dates, #project; source `Offline`). Nothing else is queued: checking off, dates, delete need the network and say so. `pwa.js` (registration, `?quick=1`, share target). Inputs have 16 px on touch screens and `touch-action: manipulation` (no iOS zoom). Bump `VERSION` in sw.js when the shell files change.
- **Notifications (Web Push)**: `Services/Notifications/PushService` (VAPID pair made at the first need and kept in `SystemSetting`; sends only to the push services of the browsers – `IsPushService`, no other address, SSRF; a 404/410 deletes the `PushDevice`), `NotificationService` (every minute: the reminder of a task with a time, `User.ReminderMinutes` before, once per due moment `Task.RemindedFor`; the daily overview at `User.DigestTime`). Page `/Account/Notifications` + `js/push.js` (subscribe/unsubscribe/test via `/api/app/push/*`), `sw.js` shows them, sets the number on the app icon and does the actions Done/Tomorrow (through the queue without a network). Headless/Playwright browsers cannot subscribe: test the server with a stand-in push service that decrypts (http_ece).
- **Live**: `LiveService` (singleton, in memory: one instance) keeps the open windows of each person (`GET /api/app/events`, Server-Sent Events, a ping every 25 s); the webhook and every change of the app API (filter `LiveAsync`) tell the people who see the project, except the window that made it (header `X-MatDo-Tab`, sent by offline.js/tasks.js). `js/live.js` reloads the page when nothing is in the way (no typing, dialog, panel, drag) and marks the new task; the stream is closed while the window is hidden. A new place that changes tasks calls `LiveService.TasksChangedAsync`.
- **Sub-tasks**: a project (and the inbox) shows them as a tree under their task (`TaskService.Tree`, `data-depth`/`data-parent`, fold in tasks.js); with a tool in effect the list is flat. Complete, delete and moving to another project take the sub-tasks along to any depth (`DescendantsAsync`).
- **Shell**: the menu switcher sits at the foot of the menu (`mat-nav__switch--foot`): *tasks*, *settings* (`/Account/*`), *admin* (`/Admin/*`, admins only); `MenuService.AreaOf` picks the panel by the address. New settings pages go into the settings panel of `_Menu`, admin pages into the admin panel – no tabs for them.

- **Database**: PascalCase tables and columns, `Id` BIGINT, `CreateDate/CreateUserId/UpdateDate/UpdateUserId` everywhere, `UpdateState` (0 deleted, 1 created, 2 updated) where soft delete makes sense,
  a `Token` (uuid) where a key is a secret. Dates of tasks are `DateOnly`/`TimeOnly` in the person's zone ("floating", like Todoist); moments are UTC.
- **GDPR**: `Services/Auth/AccountDataService` – `ExportAsync` (Art. 15/20: everything about the person as JSON, also what is in the bin, never a secret: no hash, token or key) and `EraseAsync` (Art. 17: deletes for good with `ExecuteDelete`, not the soft delete; team projects go to another member, a team keeps an owner, a team without others goes with its projects). Page `/Account/Data` (password, not the last admin); the admin delete of a user uses the same erasure. A new table with data of a person goes into both.
- **Security**: a project/task the person may not see answers 404. CSP without inline script/style. Webhook token = the secret (renewable). Registration answers do not tell who has an account
  (team add uses one message for "no account" and "already member").
- **Forms**: a second submit button (Delete) posts to `ButtonRowTagHelper.HandlerUrl` – the query of the page (`?id=5`) plus `handler=`; a bare `?handler=X` drops the id.
- **UI (MatDesign)**: CRUD = list page (toolbar above: search, filter, sort; table; pager directly under; list actions left below) + separate create/edit page with buttons Save, Back, space,
  Delete. Icons via `Ui.Icon` (toolkit sprite + `wwwroot/img/app-icons.svg`). Dependent fields `show-if`. Every `td` has `data-label`. Several controls of a kind per page get their ids from the model.
- **Code**: Microsoft C# conventions, file-scoped namespaces, English identifiers/comments, German UI texts only in the catalog. Posted texts are never null.
- **Git**: commit under the user's name only, never a Claude/Anthropic co-author trailer or "generated with" line. Branches `main` (release) and `dev` (nightly).
