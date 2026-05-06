# Order Lifecycle Phase 1 - Canonical Design

Status: Approved baseline contract for local POS orders.
Scope: Collection, Delivery, Table orders created from local POS UI.

## 1) Canonical Lifecycle

### States
- Draft: Order exists, editable, no operational commitment yet.
- Active: Order has at least one valid line and is actively being edited.
- SentPartial: Some lines sent to kitchen/bar, some still unsent.
- SentFull: All sendable lines sent to kitchen/bar.
- PaymentPartial: At least one payment captured, remaining balance > 0.
- Paid: Terminal completed state, balance = 0.
- Voided: Terminal canceled state with reason and actor.

### Allowed transitions
- Draft -> Active
- Active -> SentPartial
- SentPartial -> SentFull
- Active -> PaymentPartial
- SentPartial -> PaymentPartial
- SentFull -> PaymentPartial
- PaymentPartial -> Paid
- Draft -> Voided
- Active -> Voided
- SentPartial -> Voided
- SentFull -> Voided
- PaymentPartial -> Voided (manager authorization required)

### Forbidden transitions
- Paid -> any other state
- Voided -> any other state
- Draft -> Paid directly
- SentFull -> Draft
- Any terminal state -> editable item changes

## 2) Ownership Rules

### Collection ownership
- order_type = collection/pickup (local POS canonical = pickup)
- source_channel = local
- customer identity required
- exactly one open local order per customer context unless manager override

### Delivery ownership
- order_type = delivery
- source_channel = local
- customer identity required
- delivery address snapshot required on order
- exactly one open local order per customer+address context unless manager override

### Table ownership
- order_type = table
- source_channel = local
- table_session_id required
- exactly one open local order linked to one active table session

## 3) Invariants

- One open local order per active table session.
- Paid and Voided are terminal states.
- Terminal orders are immutable except admin correction workflow.
- Ownership fields must match order type.
- PaymentPartial implies remaining balance > 0.
- Paid implies remaining balance = 0.

## 4) Admin Correction Policy

- Paid/Voided records are never destructively overwritten.
- Corrections are event-based and auditable.
- Correction types (future phase): refund, reopen-as-new, manager-note adjustment.

## 5) Phase 1.1 Mapping to Current App Flow (No code changes yet)

This section maps lifecycle transitions to current UI entry points and handlers.

### Entry points creating order context
- Collection modal continue: Features/Customers/Pages/CollectionCustomerModal.xaml.cs (OnContinueClicked)
- Delivery modal continue: Features/Customers/Pages/DeliveryCustomerModal.xaml.cs (OnContinueClicked)
- Table cover selection: Features/Restaurant/Pages/VisualTablePage.xaml.cs (ProcessTableSelection)

### Current editing actions in OrderPlacement
- First line add trigger candidate Draft -> Active:
  - Features/Orders/Pages/OrderPlacementPageSimple.xaml.cs (OnMenuItemTappedAsync, AddItemToOrder)
- Send trigger candidates Active/SentPartial -> SentPartial/SentFull:
  - Features/Orders/Pages/OrderPlacementPageSimple.xaml.cs (OnSendClicked)
- Payment trigger candidates -> PaymentPartial/Paid:
  - Features/Orders/Pages/OrderPlacementPageSimple.xaml.cs (OnPayClicked, CompletePayment)
- Void trigger candidates -> Voided:
  - Features/Orders/Pages/OrderPlacementPageSimple.xaml.cs (OnVoidClicked)

### Known lifecycle gaps to close in later phases
- Reopen existing order from Live Orders is TODO:
  - Features/Orders/Pages/LiveOrderPage.xaml.cs (OnOrderTapped TODO comment)
- Draft/open persistence before payment is missing (currently save on payment path).
- Table close/session consistency still contains TODO operations.

## 6) User-Friendly Behavior Contract (Phase 1 UX expectations)

- Show clear status chip in header: Draft, Active, Sent Partial, Sent Full, Payment Partial, Paid, Voided.
- Prevent forbidden actions with explicit operator messages.
- Show last saved timestamp and save health indicator.
- Keep Paid/Voided pages read-only by default.

## 7) Acceptance Criteria for Phase 1 Sign-off

- Team agrees state names, meanings, and transitions.
- Team agrees ownership rules for collection/delivery/table.
- Team agrees invariants and terminal immutability.
- Team agrees that Phase 2 schema contract will enforce these rules.

---
Owner: POS architecture track
Last updated: 2026-04-21
