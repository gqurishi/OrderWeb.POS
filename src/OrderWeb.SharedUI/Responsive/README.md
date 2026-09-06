# Responsive

Shared breakpoints and adaptive-layout helpers belong here. Mother Windows,
Client Windows, Android tablets, and iPad use the same screen source and visual
identity while allowing layout regions to rearrange for available space.

`PosResponsiveLayout.ForSize` defines four shared width classes:

- Small tablet: below 801 device-independent units; compact sidebar and basket drawer.
- Medium tablet: 801–1280 units; compact sidebar and basket drawer.
- Large tablet: 1281–1600 units; expanded sidebar and docked basket.
- Desktop: 1601 units and above; Mother-sized sidebar and docked basket.

Height may compact the header, but it never selects a separate platform design.
