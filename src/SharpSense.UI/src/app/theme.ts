import { createTheme } from "@mui/material/styles";

export const theme = createTheme({
  palette: {
    mode: "dark",
    primary: { main: "#a6b3ff" },
    secondary: { main: "#80cbc4" },
    background: { default: "#0d1117", paper: "#161c26" },
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
