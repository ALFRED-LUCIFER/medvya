import * as React from "react"
import { Ban, CircleAlert, Inbox, LoaderCircle, type LucideIcon } from "lucide-react"
import { cn } from "#lib/utils"

const kinds = {
  busy: { icon: LoaderCircle, tone: "text-info-text", role: "status" },
  error: { icon: CircleAlert, tone: "text-danger-text", role: "alert" },
  empty: { icon: Inbox, tone: "text-muted-foreground", role: undefined },
  denied: { icon: Ban, tone: "text-danger-text", role: undefined },
} satisfies Record<string, { icon: LucideIcon; tone: string; role?: "status" | "alert" }>

type PageMessageProps = Omit<React.ComponentProps<"div">, "children"> & {
  kind: keyof typeof kinds
  /** A short bold line above the sentence, when the sentence alone does not say what failed ("Patients could not be loaded"). Not a heading: the page keeps its PageHeader. */
  title?: React.ReactNode
  /** One sentence: what is happening, or what is wrong and what to do. */
  children: React.ReactNode
  /** At most one next action, usually a Button. */
  action?: React.ReactNode
  /** Replaces the kind's icon, e.g. a search icon for "no patients match". */
  icon?: LucideIcon
}

/**
 * One message for busy, error, empty and denied: an icon, a sentence and at most one action (F-17, F-19). Busy is announced politely, error assertively;
 * a page that already keeps one live status region passes role={undefined}, so the change is not announced twice.
 */
function PageMessage({ kind, title, children, action, icon, className, ...props }: PageMessageProps) {
  const k = kinds[kind], Icon = icon ?? k.icon
  return (
    <div
      data-slot="page-message"
      data-kind={kind}
      role={k.role}
      aria-busy={kind === "busy" || undefined}
      className={cn("flex flex-col items-center gap-3 px-4 py-8 text-center", className)}
      {...props}
    >
      <Icon className={cn("size-8", k.tone, kind === "busy" && "motion-safe:animate-spin")} aria-hidden />
      {title && <p className="max-w-measure text-heading">{title}</p>}
      <p className="max-w-measure text-body-large">{children}</p>
      {action}
    </div>
  )
}

/** PageMessage for an empty list or page. */
const EmptyState = (props: Omit<PageMessageProps, "kind">) => <PageMessage kind="empty" {...props} />

export { PageMessage, EmptyState, type PageMessageProps }
