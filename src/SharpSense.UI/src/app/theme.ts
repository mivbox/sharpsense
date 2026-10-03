import { createTheme, type Theme } from "@mui/material/styles";

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: "class" },
  defaultColorScheme: "dark",
  colorSchemes: {
    dark: {
      palette: {
        primary: { main: "#7faf89", contrastText: "#121212" },
        secondary: { main: "#9eb5a3" },
        success: { main: "#7faf89" },
        info: { main: "#86aebe" },
        warning: { main: "#c5ab73" },
        error: { main: "#d1948d" },
        background: { default: "#121212", paper: "#1e1e1e" },
      },
    },
    light: {
      palette: {
        primary: { main: "#3f7650", contrastText: "#ffffff" },
        secondary: { main: "#596f60" },
        success: { main: "#427852" },
        info: { main: "#3d6f83" },
        warning: { main: "#85611f" },
        error: { main: "#a24842" },
        background: { default: "#f5f5f5", paper: "#ffffff" },
        text: { primary: "#212121", secondary: "#616161" },
      },
    },
  },
  components: {
    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme }) => ({ "&.Mui-selected": neutralSelection(theme) }),
      },
    },
    MuiMenuItem: {
      styleOverrides: {
        root: ({ theme }) => ({ "&.Mui-selected": neutralSelection(theme) }),
      },
    },
    MuiAutocomplete: {
      styleOverrides: {
        option: ({ theme }) => ({
          '&[aria-selected="true"]': neutralSelection(theme),
        }),
      },
    },
    MuiPaper: {
      styleOverrides: { root: { backgroundImage: "none" } },
    },
  },
  typography: {
    fontFamily: '"Inter", "Roboto", "Helvetica", "Arial", sans-serif',
    h4: { fontWeight: 600 },
    h5: { fontWeight: 600 },
    h6: { fontWeight: 600 },
    button: { textTransform: "none" },
  },
  shape: { borderRadius: 8 },
});

// Selection surfaces stay neutral while controls retain the green accent.
function neutralSelection(theme: Theme) {
  const { palette } = theme.vars ?? theme;
  return {
    backgroundColor: palette.action.selected,
    "&:hover, &.Mui-focused": {
      backgroundColor: theme.alpha(
        palette.text.primary,
        palette.action.selectedOpacity + " + " + palette.action.hoverOpacity,
      ),
    },
    "&.Mui-focusVisible": {
      backgroundColor: theme.alpha(
        palette.text.primary,
        palette.action.selectedOpacity + " + " + palette.action.focusOpacity,
      ),
    },
  };
}
