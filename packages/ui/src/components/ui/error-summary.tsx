import * as React from "react"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"

type ErrorSummaryProps = Omit<React.ComponentProps<"div">, "title"> & {
  /** Translated heading, e.g. "There is a problem". */
  title: React.ReactNode
  /**
   * Each error, linked to its Field by the control's id. Nothing renders when empty.
   * A label is shown first ("Full name: Required."), for a message that does not name its field.
   */
  errors: readonly ({ id: string; message: React.ReactNode } | { id: string; label: string; message: string })[]
  /**
   * Changes once per failed submit (React Hook Form's formState.submitCount, with shouldFocusError: false): the summary takes focus then, and only then.
   * Focus, not a live region, announces it, so fixing a field does not re-announce the list on every keystroke.
   */
  focusKey: number | string
}

/** After a failed submit: the list of errors as links to their fields; it takes focus (DESIGN.md Forms, WCAG 3.3.1). */
function ErrorSummary({ title, errors, focusKey, className, ...props }: ErrorSummaryProps) {
  const { t } = useI18n()
  const ref = React.useRef<HTMLDivElement>(null)
  const titleId = React.useId()
  const show = errors.length > 0
  // Only a new submit moves focus; errors changing while the user types must not. The ref is empty when nothing renders.
  React.useEffect(() => { ref.current?.focus() }, [focusKey])
  if (!show) return null
  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="group"
      aria-labelledby={titleId}
      data-slot="error-summary"
      className={cn("flex scroll-m-4 flex-col gap-2 rounded-lg border-2 border-danger-border bg-danger-bg p-4 text-danger-text", className)}
      {...props}
    >
      <h2 id={titleId} className="text-heading">{title}</h2>
      <ul className="flex flex-col gap-1">
        {errors.map(e => (
          <li key={e.id}>
            <a
              href={`#${e.id}`}
              className="inline-flex min-h-11 items-center text-label underline underline-offset-2"
              onClick={ev => { ev.preventDefault(); document.getElementById(e.id)?.focus() }}
            >
              {"label" in e ? t("field.errorItem", { field: e.label, error: e.message }) : e.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  )
}

export { ErrorSummary, type ErrorSummaryProps }
