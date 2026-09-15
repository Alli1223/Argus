import {
  createTheme,
  defaultVariantColorsResolver,
  type CSSVariablesResolver,
  type MantineColorsTuple,
  type VariantColorsResolver,
} from "@mantine/core";

// The palette comes from the eye of a peacock feather (see docs/ui-design.md): a blue-violet
// centre for everything interactive, and the feather's green, bronze and a crimson for status.

/** Interactive elements: links, buttons, focus. */
const iris: MantineColorsTuple = [
  "#eef0ff",
  "#dde1ff",
  "#bac2fb",
  "#95a0f5",
  "#7582ee",
  "#5f6de9",
  "#4f5be0",
  "#414cc7",
  "#3842b2",
  "#2c349c",
];

/** Healthy / online. */
const healthy: MantineColorsTuple = [
  "#e3fbf4",
  "#c6f3e6",
  "#93e6cf",
  "#5dd7b6",
  "#36c9a0",
  "#1fb98e",
  "#13a17b",
  "#0d8566",
  "#0a6b53",
  "#064f3d",
];

/** Warning. */
const bronze: MantineColorsTuple = [
  "#fff6e0",
  "#fbe9bd",
  "#f5d68a",
  "#efc257",
  "#e8b030",
  "#d99c16",
  "#c2860c",
  "#9f6c08",
  "#7e5507",
  "#5f3f05",
];

/** Critical. */
const crimson: MantineColorsTuple = [
  "#ffecea",
  "#ffd4d0",
  "#fcaaa3",
  "#f77d73",
  "#f15a4e",
  "#e64236",
  "#d0342a",
  "#ad2921",
  "#8d211b",
  "#6d1914",
];

/** Neutral greys with a trace of indigo, so light mode belongs to the same family as dark. */
const gray: MantineColorsTuple = [
  "#f5f6fa",
  "#eceef5",
  "#dfe2ee",
  "#c9cde0",
  "#aab0c9",
  "#8b92b0",
  "#6a7099",
  "#535a80",
  "#3d4366",
  "#2a2f4d",
];

/** Dark mode surfaces: a deep indigo (the feather's pupil) rather than a tinted black. */
const night: MantineColorsTuple = [
  "#dfe2f3",
  "#bfc4e0",
  "#8d93b9",
  "#6a7099",
  "#414873",
  "#333a63",
  "#262c50",
  "#1b2042",
  "#151a37",
  "#0f132b",
];

const sans = '"Archivo Variable", "Archivo", system-ui, sans-serif';

/**
 * Dark mode fills with the bright steps (iris 4 and its siblings), where white text falls below 4.5:1,
 * so filled controls take their text colour from `--argus-on-filled` instead: white in light mode,
 * deep indigo in dark mode.
 */
const variantColorResolver: VariantColorsResolver = (input) => {
  const colors = defaultVariantColorsResolver(input);
  return input.variant === "filled" && colors.color === "var(--mantine-color-white)"
    ? { ...colors, color: "var(--argus-on-filled)" }
    : colors;
};

export const theme = createTheme({
  primaryColor: "iris",
  primaryShade: { light: 7, dark: 4 },
  colors: { iris, healthy, bronze, crimson, gray, dark: night },
  variantColorResolver,

  fontFamily: sans,
  fontFamilyMonospace: '"Martian Mono Variable", "Martian Mono", ui-monospace, monospace',
  fontSizes: { xs: "12px", sm: "13px", md: "14px", lg: "16px", xl: "20px" },
  lineHeights: { xs: "1.35", sm: "1.4", md: "1.5", lg: "1.5", xl: "1.4" },
  headings: {
    fontFamily: sans,
    fontWeight: "650",
    sizes: {
      h1: { fontSize: "34px", lineHeight: "1.12" },
      h2: { fontSize: "26px", lineHeight: "1.18" },
      h3: { fontSize: "20px", lineHeight: "1.25" },
      h4: { fontSize: "16px", lineHeight: "1.3" },
    },
  },

  // Radius carries hierarchy: pills for status, small corners for controls, larger only for the watch.
  defaultRadius: "sm",
  radius: { xs: "2px", sm: "4px", md: "6px", lg: "10px", xl: "16px" },
  cursorType: "pointer",

  components: {
    // Sentence case everywhere: tags keep the case they were typed in.
    Badge: { defaultProps: { radius: "xl", variant: "light" }, styles: { root: { textTransform: "none" } } },
    Table: { defaultProps: { verticalSpacing: 6, horizontalSpacing: "sm", highlightOnHover: true } },
    Tooltip: { defaultProps: { openDelay: 250, withArrow: true } },
  },
});

export const cssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {},
  light: {
    "--mantine-color-body": gray[0],
    "--mantine-color-text": "#1a2140",
    "--argus-surface": "#ffffff",
    "--argus-border": gray[2],
    "--argus-on-filled": "#ffffff",
  },
  dark: {
    "--mantine-color-body": night[7],
    "--mantine-color-text": night[0],
    "--argus-surface": night[6],
    "--argus-border": night[5],
    "--argus-on-filled": night[8],
  },
});
