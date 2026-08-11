# Phase 4 verification

Phase 4 has two gates: automated regression checks and an on-device workflow pass. Automated checks validate the performance budgets, global responsive policy, navigation single-flight protection, stale-data subscriptions and the complete workflow/scale inventory. Physical interaction, taskbar clearance, printers, payment hardware and real local-network timing must be verified on the till PC.

Run the verification once at each Windows scaling level. The script intentionally does not change Windows display settings.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verification\Invoke-Phase4Verification.ps1 -Scaling 100
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verification\Invoke-Phase4Verification.ps1 -Scaling 125
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verification\Invoke-Phase4Verification.ps1 -Scaling 150
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verification\Invoke-Phase4Verification.ps1 -Scaling 175
```

`-ExecutionPolicy Bypass` applies only to that PowerShell process and does not alter the PC's configured policy.

At every scale:

1. Maximise the POS and confirm no footer action is behind the Windows taskbar.
2. Run every workflow in the generated result file with realistic records.
3. Double-tap each navigation entry once; only one destination may open.
4. Change an active order from another terminal, return to it, and confirm the new state appears.
5. Exercise the on-screen keyboard in customer/address, discount, notes, split payment, settings and menu forms.
6. Attach the debug `pos-debug.log` performance lines beginning with `[PERF]` and record any target failure.

A scale is not passed when a workflow is untested. A hardware-dependent test may be marked blocked with the missing device named, but it must not be recorded as passed.
