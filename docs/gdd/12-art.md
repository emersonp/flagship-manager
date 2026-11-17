# 12. Art

A chapter of the [Starship Sim GDD](../gdd.md). Section numbers are permanent ids; cross-references such as "section 4.3" find their chapter through the index.

- **Portraits**: each part is a transparent PNG drawn on a shared canvas with fixed anchor points, in greyscale where it will be tinted. Layer order is fixed: back hair, body and uniform, head, species features, face, front hair, and rank marks. Tints are applied per layer in code. A portrait is fully described by its spec, so it is reproducible and cacheable.
- **Consistency rules**: one brush, one line weight, a fixed palette for skin, hair, and uniform tints, and one light direction. A contact sheet of random portraits is generated on every part added, to catch parts that clash.
- **Coverage**: a small set multiplies fast. Six faces, eight hairstyles, four species feature sets, and three uniforms give 576 silhouettes before tinting.
- **Interface**: dark only. The palette, type, and component recipes are in `docs/design/bridge-design.md`; new screens follow them.
- **Icons and ship screen**: flat, single-weight, and monochrome with one accent colour. The ship screen is a static cutaway with department status overlaid as data.
- **Ageing**: optional overlay layers (lines, greying tint) keyed to age, so veterans visibly age.
