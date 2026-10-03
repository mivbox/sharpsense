import { IconButton, Tooltip } from "@mui/material";
import { useColorScheme } from "@mui/material/styles";
import LightModeOutlinedIcon from "@mui/icons-material/LightModeOutlined";
import DarkModeOutlinedIcon from "@mui/icons-material/DarkModeOutlined";

export function ThemeToggle() {
  const { mode, systemMode, setMode } = useColorScheme();
  const light = (mode === "system" ? systemMode : mode) === "light";
  const label = light ? "Use dark theme" : "Use light theme";

  return (
    <Tooltip title={label}>
      <IconButton
        aria-label={label}
        onClick={() => setMode(light ? "dark" : "light")}
      >
        {light ? <DarkModeOutlinedIcon /> : <LightModeOutlinedIcon />}
      </IconButton>
    </Tooltip>
  );
}
