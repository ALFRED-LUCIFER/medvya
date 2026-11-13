import * as React from "react"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"

/** "saved" needs the time the server answered, so the type cannot express a Saved the system did not earn. Absent = nothing to say yet. */
export type SaveState = { state: "saving" } | { state: "saved"; time: string } | { state: "failed" }

/**
 * The one save message, in one fixed place (MDV-P1-082). A polite live region that is always mounted, so each change is announced once;
 * the new words fade in over 150 ms (instant under reduced motion, styles.css) with no movement, no tick and no toast.
 */
function SaveStatus({ status, className, ...props }: Omit<React.ComponentProps<"p">, "children"> & { status?: SaveState }) {
  const { t } = useI18n()
  const text = !status ? ""
    : status.state === "saving" ? t("saveStatus.saving")
    : status.state === "failed" ? t("saveStatus.failed")
    : status.time ? t("saveStatus.saved", { time: status.time }) : "" // a blank time is not an answer from the server
  return (
    <p role="status" data-slot="save-status" className={cn("min-w-0 text-body tabular-nums", status?.state === "failed" ? "font-semibold text-warning-text" : "text-muted-foreground", className)} {...props}>
      {text && <span key={text} className="animate-in fade-in duration-fast ease-out">{text}</span>}
    </p>
  )
}

export { SaveStatus }
