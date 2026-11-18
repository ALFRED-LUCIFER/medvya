import * as React from "react"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"
import { type Tone } from "#components/ui/chip"
import { toothShapes } from "#components/ui/tooth-shapes"

export type Dentition = "permanent" | "primary" | "mixed"

/** How a mark paints the tooth (Medvya Dental DN-02): hatch or solid on the recorded surfaces only; dots or faded on the whole tooth. */
export type ToothFill = "hatch" | "solid" | "dots" | "faded"
/** The symbol drawn over the tooth, in the condition's tone (DN-02): root canal line, missing cross, implant screw, bridge bar, mobile arrows, fracture. */
export type ToothSymbol = "line" | "cross" | "screw" | "bar" | "arrows" | "zigzag"

/**
 * What one tooth shows: a short code that is always printed under the tooth with its surfaces ("C·DO"; never colour alone), the words for assistive
 * technology, a tone, and optionally the surfaces (letters of M D O B L), the fill and the symbol drawn on the tooth.
 */
export type ToothMark = { abbr: string; label: string; tone: Tone; surfaces?: string; fill?: ToothFill; symbol?: ToothSymbol }

export type ToothChartProps = {
  dentition: Dentition
  /** The mark of each FDI tooth ("36") that has one; a tooth without a mark is "not recorded", drawn dashed with a dash. */
  marks: Readonly<Record<string, ToothMark | undefined>>
  /** The words for a tooth with no mark, e.g. "Not recorded" (unknown is not healthy). */
  notRecorded: string
  selected: string | null
  onSelect: (tooth: string) => void
  className?: string
}

/** The code printed under a tooth: its condition and, for a surface condition, the surfaces ("C·DO"). */
const code = (mark: ToothMark) => (mark.surfaces ? `${mark.abbr}·${mark.surfaces}` : mark.abbr)

const q = (quadrant: number, teeth: number, reverse: boolean) => {
  const list = Array.from({ length: teeth }, (_, i) => `${quadrant}${i + 1}`)
  return reverse ? list.reverse() : list
}

/**
 * The rows as drawn facing the patient: the patient's right (quadrants 1 and 4, primary 5 and 8) on the viewer's left, in every language. Each row is
 * [right half, left half]; the midline sits between them.
 */
export function toothRows(dentition: Dentition): string[][][] {
  const permanentUpper = [q(1, 8, true), q(2, 8, false)], permanentLower = [q(4, 8, true), q(3, 8, false)]
  const primaryUpper = [q(5, 5, true), q(6, 5, false)], primaryLower = [q(8, 5, true), q(7, 5, false)]
  if (dentition === "permanent") return [permanentUpper, permanentLower]
  if (dentition === "primary") return [primaryUpper, primaryLower]
  return [permanentUpper, primaryUpper, primaryLower, permanentLower]
}

// The symbols of DN-02 on a 36 by 36 tooth box.
const symbols: Record<ToothSymbol, string> = {
  line: "M18 1 V35", cross: "M5 5 L31 31 M31 5 L5 31", screw: "M18 4 V32 M12 9 H24 M12 15 H24 M12 21 H24 M14 27 H22", bar: "M0 18 H36",
  arrows: "M5 18 H31 M9 13 L4 18 L9 23 M27 13 L32 18 L27 23", zigzag: "M11 3 L21 13 L14 19 L25 33",
}
const tone = (t: Tone, part: "text" | "bg" | "border") => `var(--color-${t}-${part})`
/** The pattern id a fill uses in one chart; only hatch, solid and dots need a pattern. */
const patternId = (chart: string, fill: ToothFill, t: Tone) => `${chart}-${fill}-${t}`

/**
 * One tooth drawn as itself: the outline of its type (the primary 54/55 and 64/65… as molars), mirrored for its quadrant, with the five surfaces of DN-02 inside
 * it. The surfaces are laid out for the upper right quadrant (buccal at the top, mesial towards the midline on the right), so mirroring the tooth for its quadrant
 * puts every surface on the right side for the viewer facing the patient.
 */
function ToothGlyph({ n, mark, chart }: { n: string; mark: ToothMark | undefined; chart: string }) {
  const q = Number(n[0]), i = Number(n[1]), primary = q >= 5
  const [outline, line, x, y, w, h] = toothShapes[primary ? [0, 1, 2, 3, 6, 7][i]! : i]!
  const s = (32 / Math.max(w, h)) * (primary ? 0.86 : 1)
  const fx = [2, 3, 6, 7].includes(q) ? -1 : 1, fy = [3, 4, 7, 8].includes(q) ? -1 : 1
  const x1 = x + w / 3, x2 = x + (2 * w) / 3, y1 = y + h / 3, y2 = y + (2 * h) / 3, r = x + w, b = y + h
  const zones: [surface: string, points: string][] = [
    ["B", `${x},${y} ${r},${y} ${x2},${y1} ${x1},${y1}`], ["M", `${r},${y} ${r},${b} ${x2},${y2} ${x2},${y1}`],
    ["L", `${x},${b} ${x1},${y2} ${x2},${y2} ${r},${b}`], ["D", `${x},${y} ${x1},${y1} ${x1},${y2} ${x},${b}`], ["O", `${x1},${y1} ${x2},${y1} ${x2},${y2} ${x1},${y2}`],
  ]
  const clip = `${chart}-clip-${n}`
  const fill = mark?.fill
  const paint = fill === "faded" ? tone("neutral", "bg") : fill ? `url(#${patternId(chart, fill, mark!.tone)})` : undefined
  const whole = fill === "dots" || fill === "faded"
  const thin = { vectorEffect: "non-scaling-stroke" as const }
  return (
    <svg viewBox="0 0 36 36" aria-hidden className="block h-9 w-9 shrink-0 overflow-visible" data-slot="tooth-glyph">
      <g transform={`translate(18 18) scale(${fx * s} ${fy * s}) translate(${-(x + w / 2)} ${-(y + h / 2)})`}>
        <clipPath id={clip}><path d={outline} /></clipPath>
        <path d={outline} style={{ fill: whole && paint ? paint : "var(--color-card)" }} />
        <g clipPath={`url(#${clip})`} strokeWidth={0.75} style={{ stroke: "var(--color-border)" }}>
          {zones.map(([surface, points]) => (
            <polygon key={surface} points={points} {...thin} data-surface={surface}
              style={{ fill: !whole && paint && mark?.surfaces?.includes(surface) ? paint : "transparent" }} />
          ))}
        </g>
        <path d={line} fill="none" strokeWidth={0.75} {...thin} style={{ stroke: "var(--color-muted-foreground)", opacity: 0.5 }} />
        <path d={outline} fill="none" strokeWidth={mark ? 1.5 : 1.25} strokeDasharray={mark ? undefined : "3 2"} {...thin}
          style={{ stroke: mark ? "var(--color-foreground)" : "var(--color-muted-foreground)" }} />
      </g>
      {mark?.symbol && <path d={symbols[mark.symbol]} fill="none" strokeWidth={3} strokeLinecap="round" style={{ stroke: tone(mark.tone, "text") }} />}
    </svg>
  )
}

/** The patterns the marks on this chart use (DN-02: hatch for caries, solid for fillings, dots for crowns), each in its tone. */
function Patterns({ chart, marks }: { chart: string; marks: Readonly<Record<string, ToothMark | undefined>> }) {
  const used = new Map<string, [ToothFill, Tone]>()
  for (const m of Object.values(marks)) if (m?.fill && m.fill !== "faded") used.set(patternId(chart, m.fill, m.tone), [m.fill, m.tone])
  return (
    <svg aria-hidden width="0" height="0" className="absolute">
      <defs>
        {[...used].map(([id, [fill, t]]) => (
          <pattern key={id} id={id} width="5" height="5" patternUnits="userSpaceOnUse" patternTransform={fill === "hatch" ? "rotate(45)" : undefined}>
            <rect width="5" height="5" style={{ fill: tone(t, "bg") }} />
            {fill === "hatch" && <line x1="0" y1="0" x2="0" y2="5" strokeWidth="2.4" style={{ stroke: tone(t, "text") }} />}
            {fill === "dots" && <circle cx="2.5" cy="2.5" r="1.1" style={{ fill: tone(t, "text") }} />}
            {fill === "solid" && <rect width="5" height="5" style={{ fill: tone(t, "border"), opacity: 0.45 }} />}
          </pattern>
        ))}
      </defs>
    </svg>
  )
}

/**
 * The tooth chart (MDV-P8-011, DN-02): FDI two-digit numbering, drawn facing the patient and kept left to right in Arabic too, so the quadrants never
 * mirror (the labels and panels around it do). Each tooth is drawn as its own type (incisor, canine, premolar, molar; primary 4 and 5 as molars) with its five
 * surfaces inside, the recorded surfaces painted and the condition's symbol on top, and the code printed under it with its surfaces ("C·DO"): never colour
 * alone. An unrecorded tooth is dashed with a dash (unknown is not healthy). Every tooth is a button with aria-pressed, named by what it shows and its state in
 * words ("36 C·DO, Caries (DO)"). One tab stop: arrow keys move between teeth (up and down change arch), Home and End go to a row's ends, and typing two
 * digits jumps to that tooth. 44 px wide and 80 px tall at least, for a gloved finger on a tablet.
 */
function ToothChart({ dentition, marks, notRecorded, selected, onSelect, className }: ToothChartProps) {
  const { t } = useI18n()
  const rows = React.useMemo(() => toothRows(dentition).map(([right, left]) => [...right, ...left]), [dentition])
  const all = React.useMemo(() => rows.flat(), [rows])
  const [focus, setFocus] = React.useState<string>(() => (selected && all.includes(selected) ? selected : all[0]!))
  const refs = React.useRef<Record<string, HTMLButtonElement | null>>({})
  const typed = React.useRef<{ digits: string; at: number }>({ digits: "", at: 0 })
  const chart = React.useId().replace(/:/g, "")
  const active = all.includes(focus) ? focus : all[0]!

  const move = (tooth: string, select = false) => {
    setFocus(tooth)
    refs.current[tooth]?.focus()
    if (select) onSelect(tooth)
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>, tooth: string) => {
    const r = rows.findIndex(row => row.includes(tooth))
    const c = rows[r]!.indexOf(tooth)
    const at = (row: number, col: number) => rows[row]![Math.min(col, rows[row]!.length - 1)]!
    let next: string | null = null
    if (e.key === "ArrowRight") next = rows[r]![c + 1] ?? null
    else if (e.key === "ArrowLeft") next = rows[r]![c - 1] ?? null
    else if (e.key === "ArrowDown" && r < rows.length - 1) next = at(r + 1, c)
    else if (e.key === "ArrowUp" && r > 0) next = at(r - 1, c)
    else if (e.key === "Home") next = rows[r]![0]!
    else if (e.key === "End") next = rows[r]![rows[r]!.length - 1]!
    else if (/^[0-9]$/.test(e.key)) {
      const now = Date.now()
      const digits = (now - typed.current.at < 1200 ? typed.current.digits : "") + e.key
      typed.current = { digits: digits.slice(-2), at: now }
      if (digits.length >= 2 && all.includes(digits.slice(-2))) move(digits.slice(-2), true)
      e.preventDefault()
      return
    }
    if (next) {
      e.preventDefault()
      move(next)
    }
  }

  const tooth = (n: string) => {
    const mark = marks[n]
    const pressed = selected === n
    return (
      <button
        key={n}
        ref={el => { refs.current[n] = el }}
        type="button"
        data-tooth={n}
        tabIndex={n === active ? 0 : -1}
        aria-pressed={pressed}
        // Named by what it shows, then its state in words ("36 C·DO, Caries (DO)"; "11, Not recorded"), so speech and sight agree (WCAG 2.5.3).
        aria-label={mark ? `${n} ${code(mark)}, ${mark.label}` : `${n}, ${notRecorded}`}
        onClick={() => { setFocus(n); onSelect(n) }}
        onKeyDown={e => onKeyDown(e, n)}
        className={cn(
          "flex min-h-20 min-w-11 flex-1 basis-0 flex-col items-center justify-between gap-0.5 rounded-lg border bg-card px-0.5 py-1",
          "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
          pressed ? "border-2 border-foreground bg-info-bg" : "border-border hover:bg-muted"
        )}
      >
        <span className="font-mono text-caption font-bold text-foreground">{n}</span>{mark && " "}
        <ToothGlyph n={n} mark={mark} chart={chart} />
        <span
          aria-hidden
          className={cn("min-h-4 whitespace-nowrap rounded-sm px-0.5 font-mono text-[0.625rem] font-bold leading-4", mark ? "bg-neutral-bg text-foreground" : "text-muted-foreground")}
        >
          {mark ? code(mark) : "–"}
        </span>
      </button>
    )
  }

  return (
    <div dir="ltr" role="group" aria-label={t("toothChart.label")} data-slot="tooth-chart"
      className={cn("relative flex flex-col gap-2 overflow-x-auto rounded-lg border border-border bg-card p-2", className)}>
      <Patterns chart={chart} marks={marks} />
      <div className="flex justify-between text-caption font-bold text-muted-foreground">
        <span>{t("toothChart.patientRight")}</span>
        <span>{t("toothChart.patientLeft")}</span>
      </div>
      {toothRows(dentition).map(([right, left], i, list) => (
        <React.Fragment key={i}>
          {i === list.length / 2 && <div aria-hidden className="h-0.5 rounded-full bg-border" />}
          <div role="group" className="flex items-stretch gap-1" aria-label={t(i < list.length / 2 ? "toothChart.upper" : "toothChart.lower")}>
            <div className="flex flex-1 gap-0.5">{right!.map(tooth)}</div>
            <div aria-hidden className="w-0.5 shrink-0 rounded-full bg-muted-foreground/60" />
            <div className="flex flex-1 gap-0.5">{left!.map(tooth)}</div>
          </div>
        </React.Fragment>
      ))}
      {/* Only the teeth stay left to right; the words follow their own language (Arabic reads right to left inside the chart box). */}
      <p dir="auto" className="text-caption text-muted-foreground">{t("toothChart.facing")} {t("toothChart.keys")}</p>
    </div>
  )
}

export { ToothChart }
