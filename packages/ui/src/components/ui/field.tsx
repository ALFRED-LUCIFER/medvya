import * as React from "react"
import { CircleAlert } from "lucide-react"
import { Slot } from "radix-ui"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"
import { Label } from "#components/ui/label"

/** Which fields carry a mark in words: one convention per form (DESIGN.md Forms). */
type FieldMark = "required" | "optional"
const MarkContext = React.createContext<FieldMark>("required")

/** Sets the form's convention: `required` when most fields are optional, `optional` when most are required. */
function FieldMarks({ mark, children }: { mark: FieldMark; children: React.ReactNode }) {
  return <MarkContext.Provider value={mark}>{children}</MarkContext.Provider>
}

type FieldProps = Omit<React.ComponentProps<"div">, "children"> & {
  /** The control's id; ErrorSummary links to it. */
  id: string
  label: React.ReactNode
  hint?: React.ReactNode
  /** What is wrong and what to do. Shown under the control, linked to it, and the control is marked invalid. */
  error?: React.ReactNode
  required?: boolean
  /** A unit shown beside the control, e.g. "kg". */
  unit?: React.ReactNode
  /** One control (Input, Textarea, SelectTrigger, …): it receives id, aria-describedby, aria-invalid and aria-required. */
  children: React.ReactElement
}

/** Label above the control, the form's required or optional mark in words, a hint, a unit, and the error. */
function Field({ id, label, hint, error, required, unit, className, children, ...props }: FieldProps) {
  const { t } = useI18n()
  const mark = React.useContext(MarkContext)
  const word = mark === "required" ? (required ? t("field.required") : null) : (required ? null : t("field.optional"))
  const describedBy = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ") || undefined
  const control = (
    <Slot.Root id={id} aria-describedby={describedBy} aria-invalid={error ? true : undefined} aria-required={required || undefined}>
      {children}
    </Slot.Root>
  )
  return (
    <div data-slot="field" data-invalid={error ? true : undefined} className={cn("flex min-w-0 flex-col gap-1.5", className)} {...props}>
      <Label htmlFor={id} className="inline text-label">
        {label}
        {word && <span className="font-normal text-muted-foreground"> {word}</span>}
      </Label>
      {hint && <p id={`${id}-hint`} className="text-caption text-muted-foreground">{hint}</p>}
      {unit ? <div className="flex items-center gap-2">{control}<span className="shrink-0 text-body text-muted-foreground">{unit}</span></div> : control}
      {error && (
        <p id={`${id}-error`} className="flex items-start gap-1.5 text-label text-danger-text">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          {error}
        </p>
      )}
    </div>
  )
}

export { Field, FieldMarks, type FieldProps, type FieldMark }
