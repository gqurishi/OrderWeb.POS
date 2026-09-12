# Toshiba B-FV4D Phase 3 Printing Architecture

## Scope and decision

This phase defines the architecture for container/item label printing. It does not implement TPCL commands, database migrations, APIs, or user-interface changes.

Mother POS is the sole owner of label printing. Client POS may request a label, but it never selects or contacts a physical printer. This extends the existing OrderWeb rule that Mother owns MariaDB and all network/IP printers.

The first device adapter is:

- purpose: `Label`
- technology: `Label printer`
- manufacturer/module: `Toshiba TPCL`
- exact model profile: `Toshiba B-FV4D-GS14-QM-R`
- resolution: `203 dpi`
- production transport: raw TCP/IP, port `9100`
- initial certified media profile: `60 x 40 mm`, die-cut, direct thermal

The architecture must allow later `Brother QL` and `Xprinter TSPL` adapters to consume the same label content and logical template without changing order-domain code.

## Ownership boundary

### Mother POS owns

- printer definitions, credentials where applicable, IP addresses, ports, capabilities, and enabled state;
- the default label printer and any print-group/station routing;
- exact-model and media profiles;
- validation of label requests against authoritative order and item data;
- construction of the canonical label content snapshot;
- template selection and layout resolution;
- selection of the correct manufacturer module;
- generation of printer-language bytes;
- creation and persistence of durable print jobs;
- transmission to the printer;
- retry scheduling, terminal-failure handling, and operator reprints;
- audit records, status, error details, and health information;
- the API and live status channel used by Client POS.

### Client POS owns

- identifying the order and item for which a label is requested;
- optionally identifying a specific unit/copy when the workflow permits it;
- sending one authenticated request to its paired Mother POS;
- preserving and reusing a client request ID if the same request is retried after a timeout;
- showing Mother's accepted, queued, printing, printed, or failed result.

### Client POS must never

- store or receive the Toshiba printer IP address or TCP port;
- connect directly to a printer;
- generate TPCL or another printer language;
- choose a manufacturer driver/module or physical printer;
- contain Toshiba software, drivers, tools, or configuration;
- treat a lost API response as permission to create a second logical print request.

## End-to-end flow

### Mother-originated request

`Mother order/item action -> validate authoritative data -> build label snapshot -> resolve template and route -> create durable job -> TPCL module -> TCP 9100 -> Toshiba printer -> record result -> update Mother UI`

### Client-originated request

`Client action -> authenticated Mother API request -> validate terminal/user/order/item -> deduplicate request -> build authoritative label snapshot -> resolve template and route -> create durable job -> return accepted job identity -> TPCL module -> TCP 9100 -> Toshiba printer -> persist result -> notify/poll Client -> display result`

An accepted response means the durable job exists; it does not mean the label has physically printed. The UI must distinguish `Accepted/Queued` from `Printed`.

Mother builds the printable snapshot from authoritative data. Client supplies identities and intent, not trusted printable item names, allergen text, prices, printer addresses, TPCL, or arbitrary commands.

## Component boundaries

### 1. Label content

Label content describes meaning only. It has no coordinates, printer commands, IP address, or retry information.

The canonical snapshot can contain:

- item name;
- quantity represented by this label;
- modifiers/options;
- preparation notes;
- order reference;
- table, collection, delivery, or customer reference;
- order and preparation times;
- short allergen warning;
- stable order, order-line, request, and label identifiers;
- copy number and total copies when one label is produced per container/component;
- source terminal and requesting user identifiers for audit only.

Content is snapshotted when the durable job is created. A retry uses that immutable snapshot so later order edits cannot silently change an already accepted label.

Do not place full customer details, phone numbers, addresses, payment information, or unnecessary personal data on an item label. A template may use a short collection/customer reference only when operationally required.

### 2. Logical label template

The logical template describes layout independently of TPCL, Brother commands, or TSPL. It defines:

- template ID and revision;
- media profile and orientation;
- label width and height in millimetres;
- safe margins;
- logical fields and priority;
- font role and size intent;
- alignment, maximum lines, truncation/wrapping policy, and overflow behaviour;
- field positions or layout regions;
- barcode/QR payload, position, and size when enabled;
- optional rules, separators, and emphasis;
- minimum readable allergen treatment.

The initial production template targets the certified `60 x 40 mm` media profile. `51 x 30 mm` will be a separate compact template/profile, not an automatic scale-down of the larger template.

The job records the selected template ID and revision. Editing a template affects future jobs only. Reprints of an existing job use the stored content and template revision unless an authorised operator explicitly requests regeneration using the current order and template.

Text fitting must be deterministic. Every template defines maximum lines and an explicit overflow rule. Printer adapters must not invent different wrapping rules for the same logical template.

### 3. Model/media profile

A model profile describes verified hardware capabilities separately from the logical template:

- manufacturer module and exact supported model;
- resolution in dpi;
- printable width and hardware margins;
- supported media sensing mode: gap or black mark;
- supported print speed/darkness ranges;
- tear-off/cutter capability;
- supported barcode and QR capabilities;
- character encoding and downloadable-font policy;
- approved transport types and default port.

A media profile describes the physical stock, including dimensions, sensing mode, gap/mark dimensions, orientation, material, and certification status. A job may run only when the template, media profile, and exact-model profile are compatible.

### 4. Printer module

The module converts resolved logical layout into bytes for one printer language. For Phase 3, `Toshiba TPCL`:

- receives only validated content, a frozen template revision, and the Toshiba model/media profile;
- converts millimetres to 203-dpi dots using one consistent rounding policy;
- escapes or rejects control characters and never treats user text as TPCL commands;
- applies the configured encoding/font strategy;
- generates a complete, self-contained label command stream;
- declares copies explicitly and avoids unintended duplicate output;
- returns generated bytes plus safe diagnostic metadata;
- performs no socket, database, queue, or UI work.

Raw TPCL supplied by a Client, item note, user, API request, or database content field is forbidden. Logs may record command size, module version, and a hash, but should not normally record the complete command stream because labels may contain personal information.

Later `Brother QL` and `Xprinter TSPL` modules implement the same adapter boundary. Differences in dots, fonts, rasterisation, sensing, or command languages remain inside their model/module implementations.

### 5. Network transport

Transport sends an already generated byte payload to a resolved endpoint. It knows nothing about orders, labels, layout, TPCL syntax, or retries.

For the Toshiba production path it must:

- run on Mother POS only;
- use the configured private-network IPv4/IPv6 address or resolvable host and TCP port `9100`;
- enforce connect and write timeouts;
- write the complete payload without text re-encoding or newline conversion;
- close/dispose the connection after the defined transaction;
- report structured outcomes such as connected, connect timeout, refused, write failure, or completed write;
- never interpret successful socket transmission as proof that media physically exited the printer.

Printer endpoints must come only from administrator-controlled Mother configuration. Client request data cannot override the host, port, or route. Logging must avoid credentials and redact sensitive label content.

### 6. Durable print queue

The queue is the source of truth for accepted label work. A job must be committed before Mother attempts network transmission.

Each job needs, conceptually:

- immutable job ID;
- purpose and job type;
- source (`Mother`, `Client`, automatic workflow, or authorised reprint);
- client request ID/idempotency key when applicable;
- order, order-line, and label/unit identities;
- immutable label-content snapshot;
- template ID and revision;
- printer, exact-model, module, and media profile identities/revisions;
- destination resolved by Mother;
- generated-payload hash and module version;
- status, attempt count, next-attempt time, and priority;
- created, accepted, started, completed, and last-updated times;
- requesting terminal/user and reprint relationship;
- structured error category and sanitised diagnostic message.

Payload bytes may be generated once and stored securely, or deterministically regenerated from frozen inputs. That choice must be fixed during implementation. A retry must never use mutable current order content or silently switch template/module versions.

## Queue state model

Use explicit states with auditable transitions:

`Accepted -> Queued -> Processing -> Sent`

Failure paths:

- `Processing -> RetryWaiting -> Queued` for a retryable connection/write failure;
- `Processing -> Failed` when the retry limit is exhausted or configuration/input is invalid;
- `Accepted/Queued/RetryWaiting -> Cancelled` only before a worker claims the job;
- `Failed/Sent -> new Reprint job` for an operator-approved reprint; never reset the original job.

`Sent` means the complete payload was handed to the network connection successfully. With raw port `9100`, it must not be described as physically verified unless a later printer-status protocol supplies that evidence. The UI may display `Sent to printer` rather than `Printed` where physical confirmation is unavailable.

Only one worker may claim a job at a time. Claiming and status changes require an atomic database operation with a lease/ownership timeout so an interrupted Mother process can safely recover abandoned `Processing` jobs.

## Delivery, retry, and duplicate policy

Network printing cannot guarantee exactly-once physical output: a connection can fail after the printer receives the payload but before Mother knows the result. The design therefore provides durable at-least-once processing with duplicate controls and honest status.

- Client requests use an idempotency key scoped to the paired terminal and operation. Repeating the same request returns the existing job.
- Mother UI actions must be debounced/disabled after acceptance and reuse the operation identity when recovering from a timeout.
- Automatic retries use bounded attempts and increasing delay with jitter.
- Invalid template, missing route, disabled printer, unsupported model/media combination, and payload-generation errors are terminal configuration failures, not network retries.
- Connect timeout, connection refused, and definite pre-write failure are retryable.
- A failure after partial or complete write is `OutcomeUnknown`; automatic retry must be conservative because it can duplicate a label.
- Operator reprint creates a new linked job with user, reason, and timestamp.
- Queue retention and cleanup must preserve the audit period required by OrderWeb policy while removing or minimising personal data when no longer needed.

Final attempt counts and timings will be selected during implementation and made configurable on Mother POS; they are not controlled by Client POS.

## Routing and configuration

Administrator configuration on Mother follows this hierarchy:

1. purpose: `Label`;
2. technology: `Label printer`;
3. manufacturer/module: `Toshiba TPCL`;
4. exact model: `Toshiba B-FV4D-GS14-QM-R`;
5. transport: `Raw TCP/IP`;
6. endpoint: administrator-controlled IP/host and port `9100`;
7. certified media profile;
8. default logical template;
9. print group/station and enabled state.

Routing resolves a logical label destination to one enabled printer. A job freezes that resolution when accepted. If the printer is later replaced or reconfigured, queued jobs must not silently move unless an administrator performs an explicit, audited reroute compatible with the frozen module/template/media requirements.

Secrets are not expected for raw TCP printing. If a future transport requires credentials, they remain encrypted on Mother and are never exposed to Client or stored in a printable snapshot.

## Client-to-Mother contract

The eventual API is a command/result contract rather than a generic raw-print endpoint.

The request identifies:

- client request ID;
- order ID and order-line ID;
- requested operation, such as print item label;
- desired copy/unit identity when allowed;
- authenticated paired terminal and current user context supplied by the existing Mother security layer.

Mother verifies that the order/item exists, the terminal/user may request the operation, the requested quantity is permitted, and label printing is configured. Mother then returns a job identity and current state.

The response exposes safe operational information only:

- accepted/rejected result;
- job ID;
- state and timestamps;
- safe error code and operator message;
- whether operator attention or an authorised reprint is required.

It never returns printer IP/port, TPCL, driver paths, credentials, or internal exception details. Status may be delivered through the established Mother live-update mechanism with API polling as recovery; the database remains authoritative.

## Failure categories and operator messages

Use stable categories so UI, telemetry, and support do not depend on exception text:

| Category | Retry | Operator meaning |
|---|---|---|
| `ConfigurationMissing` | No | Label printer, route, media, or template needs configuration on Mother |
| `UnsupportedCombination` | No | Selected model/module/template/media combination is not certified |
| `InvalidContent` | No | Label data cannot be safely laid out or encoded |
| `PrinterUnavailable` | Yes | Mother cannot connect to the configured printer |
| `TransportFailed` | Yes when definitely unsent | Connection or write failed |
| `OutcomeUnknown` | Manual decision | Printer may have received the label; retry could duplicate it |
| `RetryExhausted` | Manual | Automatic retry limit was reached |
| `Cancelled` | No | Job was cancelled before transmission |

Client-facing messages should say that Mother could not send the label and direct staff to the Mother POS when configuration or manual review is needed.

## Concurrency and ordering

- Jobs are serialized per physical printer so byte streams cannot interleave.
- Different physical printers may process jobs concurrently.
- Queue order is stable within a printer and priority class.
- A reprint is a new job and does not jump ahead unless an explicit policy permits it.
- Mother startup recovers expired processing leases before accepting normal worker activity.
- Only the active Mother print service processes jobs; Client instances never run the label worker.

## Observability and audit

Mother records:

- who/what requested the label and from which paired terminal;
- request/job correlation IDs;
- resolved printer/module/model/media/template revisions;
- every state transition and attempt;
- safe transport outcome, duration, and error category;
- payload byte count and hash, without routinely logging full printable content;
- reprint/cancel/reroute actor and reason.

Health checks show reachability separately from job success. A successful TCP probe does not certify media, calibration, darkness, alignment, or physical output.

## Security rules

- Label printing requires an authenticated, paired Client session or an authorised Mother user/workflow.
- Mother rechecks authorisation and current order state; it does not trust cached Client permissions.
- Free text is length-limited, normalised, and escaped by the printer module.
- No API accepts raw printer commands or arbitrary network endpoints.
- Printer management remains Mother-Administrator-only.
- The printer remains on the private POS network and is unreachable from guest/public networks.
- Audit/log access follows existing administrative permissions and data-retention policy.

## Compatibility rules for future printers

The shared layers are:

`Order/item -> canonical label content -> logical template -> resolved model/media profile -> manufacturer module -> byte payload -> transport -> durable queue/audit`

Adding Brother or Xprinter support may add:

- a manufacturer module;
- exact-model profiles;
- approved transports;
- printer-specific capability validation;
- rendered-output and physical certification tests.

It must not add Brother/Xprinter conditions to order-domain code, allow Client direct printing, redefine the canonical label content for one vendor, or make the logical template contain raw vendor commands.

Pixel-perfect output is not assumed across manufacturers. The logical information hierarchy is shared; each certified model profile may require a separately verified rendering implementation.

## Implementation sequence after approval

1. Freeze canonical label-content fields, privacy limits, and one-label-per-item/component rules.
2. Define versioned logical template and model/media profile contracts.
3. Define durable label-job persistence, state transitions, lease, idempotency, and audit schema.
4. Define the Mother-only label request/status contract and authorisation rules.
5. Implement deterministic layout resolution for the `60 x 40 mm` template.
6. Implement and unit-test the Toshiba TPCL adapter without networking.
7. Implement the raw TCP transport independently of TPCL.
8. Connect the adapter and transport through the existing Mother queue worker.
9. Add Mother printer configuration, test label, queue management, and safe status UI.
10. Add Client request/result UI without exposing printer configuration.
11. Test recovery, deduplication, outcome-unknown handling, restart leases, privacy, and security boundaries.
12. Perform physical acceptance testing with the exact Toshiba printer and certified restaurant label stock.

## Phase 3 acceptance criteria

Phase 3 architecture is approved when:

- Mother is the only owner of configuration, routing, generation, queuing, transport, retries, and audit;
- Client sends only authenticated identities/intent and cannot discover or contact the printer;
- content, logical template, model/media profile, printer module, transport, and durable queue are separate boundaries;
- accepted, sent, physically printed, failed, outcome unknown, and reprint meanings are unambiguous;
- immutable snapshots, template revisions, idempotency, per-printer serialization, and worker leases are defined;
- no user-controlled data can become executable TPCL;
- future Brother QL and Xprinter TSPL support can reuse all vendor-neutral layers;
- physical printer confirmation remains a later hardware test rather than an architectural assumption.

