import { SvgIcon } from "@mui/material";
import type { SvgIconProps } from "@mui/material";

export function SharpSenseMark(props: SvgIconProps) {
  return (
    <SvgIcon
      {...props}
      viewBox="0 0 40 40"
      data-testid="sharpsense-mark"
      sx={[
        {
          fontSize: 32,
          flexShrink: 0,
          "& .brain": { animation: "sharpsense-think 4s ease-in-out infinite" },
          "@keyframes sharpsense-think": {
            "0%, 100%": { opacity: 0.5 },
            "50%": { opacity: 1 },
          },
          "@media (prefers-reduced-motion: reduce)": {
            "& .brain": { animation: "none" },
          },
        },
        ...(Array.isArray(props.sx) ? props.sx : [props.sx]),
      ]}
    >
      <g
        fill="none"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="6" y="7" width="28" height="28" rx="8" />
        <path d="M20 4v3M3 18v7m34-7v7M16 35v2h8v-2" />
        <circle cx="20" cy="2.5" r="1.5" fill="currentColor" stroke="none" />
        <g className="brain">
          <path d="M20 12c-1.3-2.2-5-1.7-5.7.9-2.8.1-4.3 3.1-2.9 5.3-1 2.7 1.3 5 3.8 4.4 1.2 1.8 3.5 1.6 4.8-.2V12Z" />
          <path d="M20 12c1.3-2.2 5-1.7 5.7.9 2.8.1 4.3 3.1 2.9 5.3 1 2.7-1.3 5-3.8 4.4-1.2 1.8-3.5 1.6-4.8-.2M14.3 12.9l1.7 2.6m-4.6 2.7 4.1-.2m-.3 4.6 1.5-2.2m9-7.5L24 15.5m4.6 2.7-4.1-.2m.3 4.6-1.5-2.2" />
        </g>
        <path d="M13 29h3m8 0h3" strokeWidth="2.5" />
      </g>
    </SvgIcon>
  );
}
