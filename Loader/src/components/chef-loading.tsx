import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import "./chef-loading.css";

export type ChefLoadingMode = "inline" | "overlay" | "fullscreen";
export type ChefLoadingSize = "sm" | "md" | "lg";

export interface ChefLoadingProps {
  /** inline = sits next to content · overlay = covers its positioned parent · fullscreen = covers the viewport */
  mode?: ChefLoadingMode;
  size?: ChefLoadingSize;
  /** Any loading message. Defaults to "Cooking up your data…" */
  message?: string;
  /** Appearance delay in ms so quick loads never flash the loader. Default 300. */
  delay?: number;
  className?: string;
}

const SIZES: Record<ChefLoadingSize, { svg: number; text: string }> = {
  sm: { svg: 52, text: "text-xs" },
  md: { svg: 96, text: "text-sm" },
  lg: { svg: 140, text: "text-lg" },
};

function ChefArt({ px }: { px: number }) {
  return (
    <svg
      viewBox="0 0 120 120"
      width={px}
      height={px}
      role="img"
      aria-label="Chef cooking"
      className="shrink-0"
    >
      {/* steam */}
      <g fill="none" stroke="var(--cl-steam)" strokeWidth="2.5" strokeLinecap="round">
        <path className="cl-steam cl-steam-1" d="M86 40 q4 -6 0 -11" />
        <path className="cl-steam cl-steam-2" d="M97 37 q-4 -6 0 -11" />
        <path className="cl-steam cl-steam-3" d="M107 41 q4 -6 0 -10" />
      </g>

      {/* tossed food — onion ring + green chilli only, synced to the pan toss */}
      <g className="cl-food cl-food-onion">
        <circle cx="93" cy="52" r="5.6" fill="none" stroke="#f0d7b4" strokeWidth="3.2" />
        <circle cx="93" cy="52" r="2.4" fill="none" stroke="#d9b382" strokeWidth="1" />
      </g>

      <g className="cl-food cl-food-chilli">
        <path
          d="M109 49 q7.5 -1 5.5 7.2 q-1.2 4.2 -6.2 2 q-3.2 -2 -2.2 -6.2 q1 -3 2.9 -3z"
          fill="#5cb85c"
          stroke="#3f8f3f"
          strokeWidth="0.9"
        />
        <path d="M109 49 q-2.4 -2.2 -3.6 -1.2" fill="none" stroke="#4b7f2a" strokeWidth="1.3" strokeLinecap="round" />
      </g>

      <g className="cl-chef">
        <g className="cl-body">
          {/* body / coat */}
          <path
            d="M28 110 q-1 -30 19 -30 q20 0 19 30z"
            fill="var(--cl-white)"
            stroke="var(--cl-line)"
            strokeWidth="1.4"
          />
          {/* apron */}
          <path d="M34 95 q13 4 26 0 v15 h-26z" fill="var(--cl-shade)" opacity="0.75" />
          {/* neckerchief */}
          <path d="M38 75 q9 9 18 0 q-9 -5.5 -18 0z" fill="var(--cl-red)" />
          <path d="M47 80 q-4 4 -1 7 q4 -1 5 -6z" fill="var(--cl-red-dark)" />
          {/* coat buttons */}
          <circle cx="43" cy="89" r="1.7" fill="var(--cl-ink)" />
          <circle cx="43" cy="97" r="1.7" fill="var(--cl-ink)" />

          {/* resting left arm */}
          <path
            d="M32 86 q-4 8 -2 15"
            stroke="var(--cl-white)"
            strokeWidth="8"
            fill="none"
            strokeLinecap="round"
          />
          <circle cx="30.5" cy="102" r="4" fill="var(--cl-skin)" />
        </g>

        <g className="cl-head">
          {/* toque */}
          <g fill="var(--cl-white)" stroke="var(--cl-line)" strokeWidth="1.4">
            <circle cx="34" cy="24" r="10" />
            <circle cx="47" cy="17" r="12" />
            <circle cx="60" cy="24" r="10" />
            <rect x="30" y="27" width="34" height="15" rx="5.5" />
          </g>

          {/* ears */}
          <circle cx="30" cy="55" r="4.2" fill="var(--cl-skin-shade)" />
          <circle cx="64" cy="55" r="4.2" fill="var(--cl-skin-shade)" />

          {/* head */}
          <circle cx="47" cy="54" r="16" fill="var(--cl-skin)" />
          {/* hair sideburns */}
          <path d="M32 49 q2.5 -8 10 -10.5 q-6 6.5 -6.5 12.5z" fill="var(--cl-hair)" />
          <path d="M62 49 q-2.5 -8 -10 -10.5 q6 6.5 6.5 12.5z" fill="var(--cl-hair)" />
          {/* brows */}
          <path
            d="M38 45.5 q4 -2.5 8 -0.5 M52 45 q4 -2 8 0.5"
            fill="none"
            stroke="var(--cl-hair)"
            strokeWidth="1.8"
            strokeLinecap="round"
          />
          {/* smiling eyes */}
          <path
            d="M38.5 51 q3.8 -4.2 7.4 0 M51.5 51 q3.8 -4.2 7.4 0"
            fill="none"
            stroke="var(--cl-hair)"
            strokeWidth="2.3"
            strokeLinecap="round"
          />
          {/* cheeks */}
          <circle cx="35.5" cy="59.5" r="3.1" fill="#ffa98d" opacity="0.7" />
          <circle cx="59" cy="59.5" r="3.1" fill="#ffa98d" opacity="0.7" />
          {/* nose */}
          <circle cx="47.5" cy="57.5" r="3.6" fill="var(--cl-skin-shade)" />
          {/* moustache */}
          <path
            d="M47.5 61.5 q-9.5 -3.5 -12.5 3 q6.5 4.5 12.5 -1 q6 5.5 12.5 1 q-3 -6.5 -12.5 -3z"
            fill="var(--cl-hair)"
          />
          {/* open smile */}
          <path d="M42 66.5 q5.5 7 11 0z" fill="#a3282f" />
        </g>

        {/* right arm + pan — pivots together on the toss */}
        <g className="cl-arm">
          <path
            d="M62 87 q11 -1 17 -8"
            stroke="var(--cl-white)"
            strokeWidth="8.5"
            fill="none"
            strokeLinecap="round"
          />
          {/* forearm + hand */}
          <path
            d="M72 82 q6 -2 9 -6"
            stroke="var(--cl-skin)"
            strokeWidth="7"
            fill="none"
            strokeLinecap="round"
          />
          <circle cx="82" cy="75.5" r="4.6" fill="var(--cl-skin)" />
          <circle cx="82" cy="75.5" r="4.6" fill="none" stroke="var(--cl-skin-shade)" strokeWidth="0.8" />

          {/* pan handle */}
          <rect
            x="76"
            y="72.5"
            width="14"
            height="4.2"
            rx="2.1"
            fill="var(--cl-pan-dark)"
            transform="rotate(-10 83 74.6)"
          />
          {/* pan body */}
          <path d="M88 71 a16 8 0 0 0 30 0z" fill="var(--cl-pan-dark)" />
          <ellipse cx="103" cy="71" rx="15" ry="4.4" fill="var(--cl-pan)" />
          <ellipse cx="103" cy="71" rx="11.5" ry="2.9" fill="var(--cl-pan-rim)" opacity="0.55" />
        </g>
      </g>

    </svg>
  );
}

function Dots() {
  return (
    <span className="cl-dots" aria-hidden="true">
      <span className="cl-dot" />
      <span className="cl-dot cl-dot-2" />
      <span className="cl-dot cl-dot-3" />
    </span>
  );
}

export function ChefLoading({
  mode = "inline",
  size = "md",
  message = "Cooking up your data",
  delay = 300,
  className,
}: ChefLoadingProps) {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const t = setTimeout(() => setVisible(true), delay);
    return () => clearTimeout(t);
  }, [delay]);

  const s = SIZES[size];

  const layer = (
    <div className={cn("cl-layer", mode === "inline" && "cl-layer-inline")}>
      <ChefArt px={s.svg} />
      {message && (
        <p className={cn("flex items-center gap-1.5 font-bold tracking-tight", s.text)}>
          {message}
          <Dots />
        </p>
      )}
    </div>
  );

  const visibility = cn("chef-loading cl-enter", visible && "cl-visible");

  if (mode === "fullscreen") {
    return (
      <div
        role="status"
        aria-live="polite"
        className={cn(
          visibility,
          "fixed inset-0 z-50 flex items-center justify-center bg-white/92 backdrop-blur-sm",
          !visible && "pointer-events-none",
          className,
        )}
      >
        {layer}
      </div>
    );
  }

  if (mode === "overlay") {
    return (
      <div
        role="status"
        aria-live="polite"
        className={cn(
          visibility,
          "absolute inset-0 z-10 flex items-center justify-center rounded-[inherit] bg-white/88 backdrop-blur-[2px]",
          !visible && "pointer-events-none",
          className,
        )}
      >
        {layer}
      </div>
    );
  }

  return (
    <div role="status" aria-live="polite" className={cn(visibility, className)}>
      {layer}
    </div>
  );
}
