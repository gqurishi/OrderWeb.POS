# Toshiba B-FV4D Phase 1 Hardware Certification

## Scope

This document is the Phase 1 hardware gate for the first supported Toshiba label printer in OrderWeb POS.

Approved target model:

**Toshiba B-FV4D-GS14-QM-R**

OrderWeb product position:

- technology: label printer
- role: professional/high-volume kitchen container labels
- print method: direct thermal
- native application protocol: TPCL
- normal application transport: raw TCP/IP
- normal socket port: 9100
- ownership: Mother POS only

No Toshiba printer may be marked as an OrderWeb-certified printer merely because its IP address accepts a TCP connection. Certification requires the model, interfaces, media behaviour, protocol mode, and a physical test label to be verified.

## Official specification result

The target model family has the following confirmed capabilities:

| Requirement | Phase 1 result | Evidence/qualification |
|---|---|---|
| B-FV4D family | Confirmed | Exact target is B-FV4D-GS14-QM-R. |
| GS print engine | Confirmed | GS identifies the 203 dpi version. |
| Resolution | Confirmed | 203 dpi / 8 dots per mm. |
| Printing method | Confirmed | Direct thermal; no ribbon is required. |
| Ethernet/LAN | Confirmed for target model | Ethernet is a standard interface for this target; the physical RJ45 port must still be checked on the delivered unit. |
| USB | Confirmed | USB is available for configuration and diagnosis. |
| TPCL | Confirmed | TPCL is the native application command language selected for OrderWeb. |
| Raw network socket | Confirmed | Socket communication is supported. |
| Default socket port | Confirmed | Port 9100 is shown in Toshiba configuration/self-test information. |
| Gap sensor | Confirmed | Fixed transmissive gap sensor. |
| Black-mark sensor | Confirmed | Movable reflective I-mark sensor. |
| Standard issue modes | Confirmed | Continuous and tear-off modes. |
| Cutter | Optional | Full-cut and partial-cut modules are accessories and must not be assumed present. |
| Peeler | Optional | Peeler is an accessory and must not be assumed present. |
| Standard media alignment | Confirmed | Centred. |
| Supported media | Confirmed | Direct-thermal label/tag, roll and fanfold media. |
| Maximum printable width | Confirmed | 108 mm for the GS engine. |
| Media width | Confirmed | Approximately 25.4-118 mm including liner. |
| Standard roll capacity | Confirmed | Up to approximately 127 mm outside diameter. |
| Power input | Confirmed at family level | 100-240 V, 50/60 Hz through the supplied power arrangement; delivered UK/EU cord and adapter must be verified. |

Primary technical references:

- [Toshiba B-FV4 External Equipment Interface Specification](https://business.toshiba.com/downloads/KB/f1Ulds/12666/B-FV4_IF_Spec_2nd.pdf) (TPCL and network interface)
- [Toshiba B-FV4D Owner's Manual](https://www.toshibatec.com/ovs-support/bcs/om/B-FV4D/FV4D-GL_OM_EN_0000.pdf)
- [Toshiba B-FV4 Setting Tool](https://www.toshibatec.com/download_overseas/printer/setting_tool/B-FV4_Series/)
- [Toshiba B-FV4 203 dpi TPCL Windows driver supported-model list](https://business.toshiba.com/downloads/KB/f1Ulds/16383/Printer_Driver_V20181M3_19thA.pdf)

## Exact purchase specification

The purchase order and seller invoice must state:

**Toshiba B-FV4D-GS14-QM-R, 203 dpi, direct thermal, USB + Ethernet/LAN**

The following substitutions must not be accepted without a new review:

- B-FV4T thermal-transfer model
- TS 300 dpi model
- B-EV4 or another Toshiba family
- USB-only configuration
- a refurbished unit with an unknown interface board
- a visually similar unit whose rating plate does not show the exact model

## Evidence required from the seller

Before purchase or dispatch, obtain:

1. A clear photograph of the product rating/model label.
   The model text must read `B-FV4D-GS14-QM-R`.
2. A clear photograph of the rear and side connection panel.
   The RJ45 Ethernet socket, USB connection, power connection, and any fitted optional interface must be visible.
3. Written confirmation that Ethernet/LAN is operational and not merely an optional interface omitted from the supplied unit.
4. Written confirmation that the unit is supplied with a UK three-pin power lead, or the correct approved UK lead and compatible Toshiba power supply.
5. Written confirmation of new/refurbished status and warranty duration.
6. A list of included accessories.
7. Written confirmation of whether a cutter is fitted:
   - no cutter / tear-off only;
   - full cutter; or
   - partial cutter.
8. Written confirmation of whether a peeler is fitted.
9. Firmware version and configuration/self-test page if the seller can provide it.
10. Confirmation that the printer can operate in TPCL mode with socket communication enabled on TCP port 9100.

## Physical receiving inspection

When the unit arrives, the installer must verify:

- model/rating label matches the purchase order;
- casing, printhead, platen roller, sensors, connections, and power supply are undamaged;
- RJ45 Ethernet port is physically present;
- correct Toshiba power supply and UK power lead are present;
- USB cable is available for initial configuration if needed;
- optional cutter or peeler matches the purchased configuration;
- feed button and both status LEDs operate;
- printer completes its self-test without a hardware error;
- self-test/configuration output shows the expected model and 203 dpi engine;
- LAN information is present;
- socket communication can be enabled;
- socket port is 9100;
- TPCL mode/version is present;
- gap and reflective sensor calibration both complete successfully using suitable test media.

Record the following in the installation report:

- full model number;
- serial number;
- MAC address;
- firmware/program version;
- TPCL version;
- assigned IP address;
- subnet mask and gateway;
- DHCP or reservation/static-address method;
- socket port;
- installed options;
- label stock manufacturer and product code;
- label width, height, liner width, gap, core size, and roll diameter;
- self-test result;
- installer name and date.

## Phase 1 media baseline

The initial application profile will target a die-cut, direct-thermal, gap-sensed kitchen label.

Preferred initial size:

**60 mm x 40 mm**

Before purchasing production quantities, the exact stock must pass:

- gap-sensor calibration;
- centred feeding without sideways movement;
- readable 203 dpi printing;
- adhesion to the restaurant's plastic, card, paper, and foil containers as applicable;
- adhesion to cold and warm external container surfaces;
- removal/permanence behaviour required by the restaurant;
- no direct food contact unless the stock is specifically certified for it;
- readability for the expected preparation and delivery period.

The supplier must provide the label-stock technical data sheet, adhesive type, operating temperature, application temperature, and food-contact statement.

## Tear-off and cutter decision

Phase 1 does not require a cutter. The base certified workflow is die-cut labels in tear-off mode.

Recommended initial choice:

- use tear-off mode for the first implementation and hardware test;
- do not purchase a cutter until staff workflow has been observed;
- if a cutter is required, record the exact Toshiba cutter accessory and certify it separately;
- the POS must never issue cutter commands unless the printer record says a verified cutter is installed.

## Network acceptance gate

The printer must be placed on the private POS network and receive a stable address by static assignment or DHCP reservation.

Required checks:

- Mother POS can reach the assigned IP;
- TCP port 9100 accepts a connection;
- socket communication is enabled in the printer;
- guest/public networks cannot reach the printer;
- no Client POS is configured to print directly to it;
- address remains unchanged after printer and router restart;
- a TPCL test job produces a physical label;
- disconnecting Ethernet produces a visible failure rather than a false success.

A successful ping or TCP connection alone is not sufficient acceptance.

## Windows support software on Mother

Install on the Mother PC for commissioning and support:

- official Toshiba B-FV4 Setting Tool;
- official Toshiba B-FV4 203 dpi TPCL Windows driver;
- optional licensed Toshiba/BarTender label-design utility when required.

These tools are for IP configuration, media calibration, Windows test printing, diagnosis, and emergency fallback. Normal OrderWeb printing will use the built-in Mother TPCL module and will not depend on the Windows print driver.

Nothing Toshiba-specific is installed on Client POS computers.

## Phase 1 pass/fail decision

### Specification status

**Conditionally passed for development.**

The published model specification meets the OrderWeb requirements for 203 dpi direct-thermal TPCL label printing over Ethernet and raw TCP port 9100.

### Outstanding unit-level evidence

Development may start against this model profile, but production certification remains blocked until all of the following are received and checked:

- rating-label photograph showing `B-FV4D-GS14-QM-R`;
- rear-port photograph showing RJ45 Ethernet;
- UK/EU power confirmation;
- installed cutter/peeler declaration;
- physical printer or remote access to it;
- Toshiba self-test/configuration page;
- sample 60 x 40 mm label stock technical data;
- successful physical TPCL test label.

### Phase 1 completion owner

The purchaser/installer supplies the unit-level evidence. The OrderWeb implementation owner records it here and signs off the hardware gate before production approval.
