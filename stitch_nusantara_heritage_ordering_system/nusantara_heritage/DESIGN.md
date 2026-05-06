---
name: Nusantara Heritage
colors:
  surface: '#fcf9f8'
  surface-dim: '#dcd9d9'
  surface-bright: '#fcf9f8'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f6f3f2'
  surface-container: '#f0eded'
  surface-container-high: '#eae7e7'
  surface-container-highest: '#e5e2e1'
  on-surface: '#1c1b1b'
  on-surface-variant: '#584238'
  inverse-surface: '#313030'
  inverse-on-surface: '#f3f0ef'
  outline: '#8c7166'
  outline-variant: '#e0c0b2'
  surface-tint: '#a04100'
  primary: '#9c3f00'
  on-primary: '#ffffff'
  primary-container: '#c45100'
  on-primary-container: '#fffbff'
  inverse-primary: '#ffb693'
  secondary: '#5e604d'
  on-secondary: '#ffffff'
  secondary-container: '#e1e1c9'
  on-secondary-container: '#636451'
  tertiary: '#006c0c'
  on-tertiary: '#ffffff'
  tertiary-container: '#1c871e'
  on-tertiary-container: '#f8fff0'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#ffdbcc'
  primary-fixed-dim: '#ffb693'
  on-primary-fixed: '#351000'
  on-primary-fixed-variant: '#7a3000'
  secondary-fixed: '#e4e4cc'
  secondary-fixed-dim: '#c8c8b0'
  on-secondary-fixed: '#1b1d0e'
  on-secondary-fixed-variant: '#474836'
  tertiary-fixed: '#92fa83'
  tertiary-fixed-dim: '#77dd6a'
  on-tertiary-fixed: '#002201'
  on-tertiary-fixed-variant: '#005307'
  background: '#fcf9f8'
  on-background: '#1c1b1b'
  surface-variant: '#e5e2e1'
typography:
  h1:
    fontFamily: notoSerif
    fontSize: 48px
    fontWeight: '700'
    lineHeight: '1.2'
  h2:
    fontFamily: notoSerif
    fontSize: 36px
    fontWeight: '600'
    lineHeight: '1.3'
  h3:
    fontFamily: notoSerif
    fontSize: 24px
    fontWeight: '600'
    lineHeight: '1.4'
  body-lg:
    fontFamily: inter
    fontSize: 18px
    fontWeight: '400'
    lineHeight: '1.6'
  body-md:
    fontFamily: inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.6'
  body-sm:
    fontFamily: inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
  label-caps:
    fontFamily: inter
    fontSize: 12px
    fontWeight: '600'
    lineHeight: '1.2'
    letterSpacing: 0.05em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  base: 8px
  xs: 4px
  sm: 12px
  md: 24px
  lg: 48px
  xl: 80px
  container-max: 1280px
  gutter: 24px
---

## Brand & Style

The design system evokes a sense of "Modern Indonesian Luxury"—a balance between ancestral tradition and contemporary fine dining. It targets a discerning audience seeking an authentic yet elevated culinary experience. The emotional response is one of warmth, hospitality, and grounded elegance.

The UI style blends **Minimalism** with **Tactile** elements. By utilizing large amounts of whitespace (rendered in warm beige) and high-quality photography, the interface recedes to let the vibrant colors of Indonesian cuisine take center stage. Soft shadows and organic textures create an inviting, physical presence that mirrors the atmosphere of a premium restaurant.

## Colors

The palette is rooted in the natural landscapes and materials of Indonesia. 
- **Terracotta Orange (#CC5500):** Used as the primary action color, representing earth and fire. It draws the eye to key interactions.
- **Warm Beige (#F5F5DC):** The foundation of the design system, used for large background surfaces to provide a softer, more premium alternative to pure white.
- **Soft Black (#1A1A1A):** Used for primary typography and deep grounding elements, ensuring high legibility and a sophisticated weight.
- **Forest Green (#228B22):** An accent color used sparingly for "freshness" indicators, vegetarian markers, or success states.

Backgrounds should primarily use the Warm Beige, while cards and modals use white surfaces to create subtle contrast and depth.

## Typography

This design system utilizes a high-contrast typographic pairing. **Noto Serif** provides a timeless, editorial feel for headings, suggesting a legacy of craftsmanship. **Inter** is used for all functional text, including body copy, descriptions, and labels, ensuring maximum readability across digital devices.

Apply Noto Serif to item titles and section headers. Use Inter for ingredient lists, prices, and navigation items. For secondary information like "Table Number" or "Category Label," use `label-caps` in uppercase to distinguish metadata from content.

## Layout & Spacing

The layout follows a **Fixed Grid** model for desktop, centered on the screen to maintain a boutique, curated feel. On smaller screens, the layout transitions to a fluid model with generous margins.

A 12-column grid is used for desktop web views, with food cards typically spanning 3 or 4 columns. Spacing is intentional and airy; use `lg` and `xl` units between major sections to prevent the interface from feeling "cluttered" or "fast-food." Vertical rhythm should be strictly maintained using the 8px base unit.

## Elevation & Depth

Visual hierarchy is established through **Ambient Shadows** and tonal layering. 
- **Tier 1 (Surface):** The Warm Beige background acts as the canvas.
- **Tier 2 (Cards):** Food cards use a white background with a very soft, diffused shadow (15% opacity Soft Black, 20px blur, 4px Y-offset) to appear lifted.
- **Tier 3 (Floating Actions):** Circular "add" buttons and navigation elements use a slightly crisper shadow to suggest high interactability.
- **Tier 4 (Modals/Drawers):** These use a Soft Black backdrop at 40% opacity to dim the background, focusing attention on the high-fidelity photography within the modal.

## Shapes

The shape language is **Rounded**, reflecting the organic nature of food and hospitality. 
- **Standard Cards:** 1rem (16px) corner radius to soften the edges of photography.
- **Buttons:** Large buttons use a 0.5rem (8px) radius, while the specific "Add" buttons are perfectly circular.
- **Category Tabs:** Pill-shaped (fully rounded) to differentiate them from functional action buttons.
- **Input Fields:** Softly rounded at 0.5rem to match the secondary button style.

## Components

### Navigation Bar
The top navigation features the brand logo centered or left-aligned, with a persistent "Table Info" chip (e.g., "Table 12") on the right. Use a transparent background that blurs into the beige on scroll.

### Category Tabs
Use a horizontal scrolling list of pill-shaped chips. The active state should be the Soft Black with white text, while inactive states are beige with a thin terracotta border.

### Food Cards
Cards must feature high-quality imagery at the top. The "Add" button is a circular floating element positioned in the bottom-right corner of the image area, using the Terracotta Orange. Price should be displayed in a bold Inter font.

### Buttons & Inputs
- **Primary Button:** Terracotta Orange background with white text.
- **Secondary Button:** Warm Beige background with a Soft Black border.
- **Input Fields:** Clean white background with a subtle 1px border in a muted terracotta or soft grey.

### Modals & Drawers
Food details should open in a bottom drawer on mobile and a center modal on desktop. These must feature a "Hero" image area that takes up at least 40% of the view, allowing the photography to sell the dish's quality.