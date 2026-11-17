import * as React from "react"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"
import { toneClasses, type Tone } from "#components/ui/chip"

export type Dentition = "permanent" | "primary" | "mixed"

/** What one tooth shows: a short code that is always printed on the tooth (never colour alone), the words for assistive technology, and a tone. */
export type ToothMark = { abbr: string; label: string; tone: Tone }

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

/**
 * The tooth chart (MDV-P8-011, DN-02): FDI two-digit numbering, drawn facing the patient and kept left to right in Arabic too, so the quadrants never
 * mirror (the labels and panels around it do). Every tooth is a button with aria-pressed, named by what it shows and its state in words ("36 C, Caries"); a
 * recorded tooth always prints its code
 * (C, F, RCT…) and an unrecorded one is dashed with a dash. One tab stop: arrow keys move between teeth (up and down change arch), Home and End go to a
 * row's ends, and typing two digits jumps to that tooth. 44 px wide and 64 px tall at least, for a gloved finger on a tablet.
 */
function ToothChart({ dentition, marks, notRecorded, selected, onSelect, className }: ToothChartProps) {
  const { t } = useI18n()
  const rows = React.useMemo(() => toothRows(dentition).map(([right, left]) => [...right, ...left]), [dentition])
  const all = React.useMemo(() => rows.flat(), [rows])
  const [focus, setFocus] = React.useState<string>(() => (selected && all.includes(selected) ? selected : all[0]!))
  const refs = React.useRef<Record<string, HTMLButtonElement | null>>({})
  const typed = React.useRef<{ digits: string; at: number }>({ digits: "", at: 0 })
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
        // Named by what it shows, then its state in words ("36 C, Caries (DO)"; "11, Not recorded"), so speech and sight agree (WCAG 2.5.3).
        aria-label={mark ? `${n} ${mark.abbr}, ${mark.label}` : `${n}, ${notRecorded}`}
        onClick={() => { setFocus(n); onSelect(n) }}
        onKeyDown={e => onKeyDown(e, n)}
        className={cn(
          "flex min-h-16 min-w-11 flex-1 basis-0 flex-col items-center justify-between gap-1 rounded-md border-2 border-transparent px-0.5 py-1",
          "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
          pressed ? "border-foreground bg-muted" : "hover:bg-muted"
        )}
      >
        <span className="font-mono text-caption font-bold text-foreground">{n}</span>{mark && " "}
        <span
          aria-hidden
          className={cn(
            "flex min-h-7 w-full max-w-10 items-center justify-center rounded-sm border font-mono text-caption font-bold",
            mark ? toneClasses[mark.tone] : "border-dashed border-muted-foreground/60 bg-card text-muted-foreground"
          )}
        >
          {mark?.abbr ?? "–"}
        </span>
      </button>
    )
  }

  return (
    <div dir="ltr" role="group" aria-label={t("toothChart.label")} data-slot="tooth-chart"
      className={cn("flex flex-col gap-2 overflow-x-auto rounded-lg border border-border bg-card p-2", className)}>
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
