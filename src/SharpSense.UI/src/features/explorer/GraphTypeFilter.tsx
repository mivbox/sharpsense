import { useState } from "react";
import {
  Button,
  Checkbox,
  Divider,
  ListItemText,
  Menu,
  MenuItem,
} from "@mui/material";
import FilterListRoundedIcon from "@mui/icons-material/FilterListRounded";

export function GraphTypeFilter({
  counts,
  selected,
  onChange,
}: {
  counts: ReadonlyMap<string, number>;
  selected: string[];
  onChange: (types: string[]) => void;
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const all = selected.includes("*");
  const types = [...new Set([...counts.keys(), ...selected])]
    .filter((type) => type !== "*")
    .sort();

  const toggle = (type: string) => {
    const current = new Set(all ? types : selected);
    if (current.has(type)) current.delete(type);
    else current.add(type);
    onChange([...current].sort());
  };

  return (
    <>
      <Button
        size="small"
        variant="outlined"
        startIcon={<FilterListRoundedIcon />}
        onClick={(event) => setAnchor(event.currentTarget)}
        aria-haspopup="menu"
        aria-expanded={Boolean(anchor)}
        aria-label="Visible node types"
        data-testid="graph-type-filter"
        sx={{ flexShrink: 0 }}
      >
        {all ? "All types" : `${selected.length} types`}
      </Button>
      <Menu
        anchorEl={anchor}
        open={Boolean(anchor)}
        onClose={() => setAnchor(null)}
        slotProps={{ list: { "aria-label": "Visible node types" } }}
      >
        <MenuItem onClick={() => onChange(all ? [] : ["*"])}>
          <Checkbox checked={all} tabIndex={-1} disableRipple size="small" />
          <ListItemText primary="All types" />
        </MenuItem>
        <Divider />
        {types.map((type) => (
          <MenuItem key={type} onClick={() => toggle(type)}>
            <Checkbox
              checked={all || selected.includes(type)}
              tabIndex={-1}
              disableRipple
              size="small"
            />
            <ListItemText
              primary={type[0].toUpperCase() + type.slice(1)}
              secondary={`${(counts.get(type) ?? 0).toLocaleString()} loaded`}
            />
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
