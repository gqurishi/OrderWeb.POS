# Toshiba B-FV4D Phase 2 Official Resources

## Decision

OrderWeb POS will not redistribute Toshiba manuals, utilities, drivers, firmware, or other Toshiba software in the application or installer without Toshiba's prior written permission.

Public technical documents have been downloaded for local engineering review into:

`.codex-artifacts/toshiba-bfv4d/official-docs/`

That directory is a local working area, not an application resource, release payload, or approved redistribution location. The canonical source of every resource remains Toshiba's official support site.

The B-FV4 Setting Tool and TPCL Windows driver must be downloaded and installed separately by the restaurant's authorised installer after accepting Toshiba's current licence for the purchased printer.

## Resource register

| Resource | Status | Canonical source | Local engineering copy |
|---|---|---|---|
| B-FV4 External Equipment Interface Specification | Obtained | [Toshiba official TPCL/interface PDF](https://business.toshiba.com/downloads/KB/f1Ulds/12666/B-FV4_IF_Spec_2nd.pdf) | Local review only; do not package or redistribute |
| B-FV4D operator manual | Obtained | [Toshiba official B-FV4D manual](https://www.toshibatec.com/ovs-support/bcs/om/B-FV4D/FV4D-GL_OM_EN_0000.pdf) | Local review only; this is the currently hosted GL-family manual. Obtain the exact GS14 manual from the supplier before commissioning; do not package or redistribute |
| B-FV4 product/media brochure | Obtained | [Toshiba official B-FV4 brochure](https://www.toshibatec.com/ovs-support/Brochure/BCP/BR_B-FV4_Print_20180126.pdf) | Local review only; do not package or redistribute |
| B-FV4 Setting Tool | Link verified; not downloaded or bundled | [Toshiba official B-FV4 Setting Tool page](https://www.toshibatec.com/download_overseas/printer/setting_tool/B-FV4_Series/) | Installer must accept the licence and download directly |
| B-FV4 203 dpi TPCL Windows driver | Model support verified; installer not downloaded or bundled | [Toshiba Tec Europe driver search](https://www.toshibatec.eu/support/drivers/SearchDriver?searchString=B-FV4D) | Installer must accept the current licence and download directly |
| Driver supported-model documentation | Link recorded | [Toshiba official supported-model PDF](https://business.toshiba.com/downloads/KB/f1Ulds/16383/Printer_Driver_V20181M3_19thA.pdf) | The list identifies `B-FV4D-GS14-QM-R` under `TOSHIBA B-FV4 (203 dpi)` |
| Firmware/update package | Not required or obtained at this stage | Obtain only from Toshiba Tec or an authorised Toshiba dealer for the exact serial number and hardware revision | Never bundle; never install an unverified image |
| Exact label-stock compatibility data sheet | Pending supplier selection | Obtain from the chosen label converter/manufacturer for the exact face/adhesive/liner construction | Required before media certification |

## Download verification

The following hashes identify the official documents downloaded for the Phase 2 engineering review on 12 September 2026. A future download may legitimately have a different hash if Toshiba revises a document; in that case record the new version, source, date, and hash before replacing the engineering reference.

| File | Size (bytes) | SHA-256 |
|---|---:|---|
| `B-FV4_IF_Spec_2nd.pdf` | 10,537,528 | `E0FD632EE9F6F9D435EF57D432F6206C1C4D2DC5EE083267DE8D17E80ECE6D22` |
| `B-FV4D_GL_Owners_Manual_EN.pdf` | 1,663,231 | `EE1EA25FBADCF9E7BA1C6612FAFA5ADEE15251F32765CB89E6AF2C21F9A9A12E` |
| `B-FV4_Product_Brochure.pdf` | 1,816,040 | `E0848FD60807D17EA42B477BA41545228F2FC1DD367A1EAB1D260B7C5C33541C` |

## Licensing review

The Toshiba Setting Tool download page states, among other conditions, that:

- the software is for use with a Toshiba Tec barcode printer;
- its use is limited to the licensed/acquired printer context described by Toshiba;
- it must not be sublicensed, distributed, transferred, lent, or otherwise provided to a third party except where Toshiba expressly permits it;
- it must not be copied or duplicated, including as a backup, except where Toshiba expressly permits it;
- it must not be modified, reverse engineered, reverse compiled, or disassembled;
- downloading/using it requires acceptance of Toshiba's agreement.

The operator manual also states that it may not be copied in whole or in part without prior written permission from Toshiba Tec.

Consequences for OrderWeb:

1. Do not commit Toshiba executables, installers, manuals, or PDFs to the distributable application repository.
2. Do not embed Toshiba files as application resources.
3. Do not place Toshiba files in `Installer`, `ReadyToCopy`, an MSIX, deployment ZIP, update package, or release artefact.
4. Do not mirror Toshiba downloads on an OrderWeb server.
5. Do not automatically accept Toshiba's licence on behalf of a restaurant or installer.
6. Link to Toshiba's official pages and require the authorised installer to review and accept the current terms.
7. If offline bundling is commercially required, obtain explicit written redistribution permission from Toshiba Tec first and retain that permission in the product compliance records.

This is a product packaging decision based on the published licence language, not legal advice. Material changes to the licence must be reviewed before a release process changes.

## Firmware policy

No firmware update is part of the first installation by default.

Before any firmware update:

- read the installed firmware/program version from the printer self-test;
- record the full printer model, serial number, region, interface configuration, and current version;
- obtain the firmware and procedure from Toshiba Tec or an authorised Toshiba service provider;
- verify that the package explicitly supports `B-FV4D-GS14-QM-R` and the unit's hardware revision;
- record the package hash and release notes;
- back up/export permitted printer settings using Toshiba's supported procedure;
- use stable power and a direct, supported connection during the update;
- repeat the complete printer, network, calibration, and label acceptance test afterward.

Do not load firmware intended for B-FV4T, a 300 dpi TS model, B-EV4, B-FV4D-GL, or another region/model unless Toshiba explicitly confirms compatibility with the exact unit.

## Mother-PC installation checklist

The authorised installer performs these steps on the Mother PC only:

1. Confirm the physical printer rating label reads `B-FV4D-GS14-QM-R`.
2. Open the official Toshiba B-FV4 Setting Tool page.
3. Read and accept the current Toshiba licence as the printer owner/authorised installer.
4. Download the B-FV4 Setting Tool directly from Toshiba.
5. Verify the downloaded publisher/signature and scan the file with the organisation's security tooling.
6. Record version `V1.0.41.146` if that remains Toshiba's current offered B-FV4 version; otherwise record the newer official version.
7. Install the tool on the Mother PC or use an authorised service laptop.
8. Open the official Toshiba driver page and select the B-FV4 203 dpi TPCL driver that explicitly lists `B-FV4D-GS14-QM-R`.
9. Read and accept the current driver licence.
10. Download directly from Toshiba, verify publisher/signature, scan, and record version/hash.
11. Install the Windows printer using a Standard TCP/IP port that points to the printer's reserved/static address.
12. Name the Windows fallback printer `OrderWeb - Toshiba Kitchen Labels`.
13. Configure the certified media dimensions and print a Windows diagnostic label.
14. Install no Toshiba software on Client POS computers.
15. Record the completed installation in the site commissioning report.

## Application boundary

The official Windows driver is not the normal OrderWeb printing path.

Normal production:

`Client/Mother order -> Mother durable queue -> OrderWeb TPCL module -> TCP 9100 -> Toshiba printer`

Diagnostic fallback:

`Mother Windows test/tool -> licensed Toshiba Windows driver -> Toshiba printer`

The future built-in TPCL module will be original OrderWeb application code based on the public Toshiba interface specification. It must not contain Toshiba executable code, extracted driver code, copied proprietary source code, or bundled Toshiba binaries.

## Phase 2 exit criteria

Phase 2 is complete for development when:

- official TPCL/interface, operator, and media/product documentation is registered;
- local engineering copies have hashes and remain outside release packaging;
- the driver and Setting Tool official sources are documented;
- the no-redistribution decision is enforced in the implementation and installer plans;
- firmware is explicitly deferred unless the physical unit requires an approved update;
- the Mother-PC manual installation checklist exists;
- the exact label-stock data sheet remains a tracked prerequisite for physical certification.

Phase 2 production commissioning remains pending until an authorised installer downloads and installs the current Toshiba Setting Tool and B-FV4 203 dpi TPCL Windows driver for the acquired printer under Toshiba's current licence.
