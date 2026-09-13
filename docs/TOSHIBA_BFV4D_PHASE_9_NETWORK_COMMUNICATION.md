# Toshiba B-FV4D Phase 9 — network communication

## Production connection

The supported production route is Mother POS to the printer over wired Ethernet and raw TCP, normally port 9100. Printer setup already rejects public addresses. Installations should use a private static IPv4 address or a DHCP reservation so the saved endpoint does not move.

## Timeout boundaries

`ToshibaNetworkTimeouts` defines four independent defaults:

- connection: 5 seconds;
- data send: 10 seconds;
- status response: 3 seconds;
- complete job, including endpoint-lock wait: 30 seconds.

All values are configurable when constructing the transport and must be positive. The complete-job timeout is the outer deadline.

## Concurrency and durability

`ToshibaRawTcpTransport` owns a process-wide lock for each `IPv4:port` endpoint. Only one job may connect/write/query status for an endpoint at once; different printers can work concurrently.

`ToshibaLabelNetworkService` prepares and hashes TPCL, records the durable attempt, sends it, requests status and records the structured outcome. Jobs whose bytes were accepted but whose physical output is not confirmed become `outcome_unknown`, not failed. They are excluded from automatic retry to prevent duplicate labels.

## Result levels

Each attempt records these independently:

- network reachable;
- TCP port reachable;
- expected Toshiba protocol confirmed;
- data accepted by the local network stream;
- physical label confirmed by an operator;
- raw printer status/error response.

Protocol confirmation requires the documented 23-byte `WB` response envelope: SOH, STX, status type `3`, declared length `23`, CR and LF. A random or malformed response is retained for diagnosis but does not confirm TPCL.

A TCP connection, successful write, or valid current-status reply does not prove that a particular label physically printed. `ConfirmPhysicalLabelAsync` is the separate operator-confirmation action and records the timestamp against both the job and printer.
