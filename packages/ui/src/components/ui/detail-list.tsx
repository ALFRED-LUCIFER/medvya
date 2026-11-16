import * as React from "react"
import { cn } from "#lib/utils"

/**
 * The four ways a label and its value are laid out (dl/dt/dd):
 * stacked: label above value, two columns from md; inline: label beside value; rows: a bordered list, one pair per row;
 * tiles: bordered tiles with a large value and an optional note (counts, metrics).
 */
const lists = {
  stacked: "grid gap-x-6 gap-y-3 md:grid-cols-2",
  inline: "grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1",
  rows: "flex flex-col divide-y rounded-lg border",
  tiles: "grid gap-4 md:grid-cols-2",
} as const
type DetailLayout = keyof typeof lists

const parts: Record<DetailLayout, { item: string; label: string; value: string }> = {
  stacked: { item: "flex min-w-0 flex-col", label: "text-caption font-bold text-muted-foreground", value: "break-words" },
  // display: contents lets each dt and dd sit in the list's two-column grid while the pair stays one element.
  inline: { item: "contents", label: "text-muted-foreground", value: "min-w-0 break-words" },
  rows: { item: "flex min-h-12 flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2", label: "w-36 shrink-0 font-semibold", value: "min-w-0 flex-1 break-words" },
  tiles: { item: "flex flex-col gap-1 rounded-lg border p-4", label: "font-bold", value: "text-heading font-extrabold" },
}

const LayoutContext = React.createContext<DetailLayout>("stacked")

type DetailListProps = React.ComponentProps<"dl"> & { layout?: DetailLayout }

/** A list of labels and their values. Put Detail items in it. */
function DetailList({ layout = "stacked", className, ...props }: DetailListProps) {
  return (
    <LayoutContext.Provider value={layout}>
      <dl data-slot="detail-list" data-layout={layout} className={cn(lists[layout], className)} {...props} />
    </LayoutContext.Provider>
  )
}

type DetailProps = Omit<React.ComponentProps<"div">, "children"> & {
  label: React.ReactNode
  /** The value. */
  children: React.ReactNode
  /** A muted line under the value (tiles, stacked). */
  note?: React.ReactNode
  /** A decorative icon before the label (rows). */
  icon?: React.ReactNode
  /** A button at the end of the row (rows), e.g. Edit. */
  action?: React.ReactNode
  labelClassName?: string
  valueClassName?: string
  valueProps?: Omit<React.ComponentProps<"dd">, "children" | "className">
}

function Detail({ label, children, note, icon, action, labelClassName, valueClassName, valueProps, className, ...props }: DetailProps) {
  const p = parts[React.useContext(LayoutContext)]
  return (
    <div data-slot="detail" className={cn(p.item, className)} {...props}>
      {icon}
      <dt className={cn(p.label, labelClassName)}>{label}</dt>
      <dd className={cn(p.value, valueClassName)} {...valueProps}>{children}</dd>
      {note && <dd className="text-body text-muted-foreground">{note}</dd>}
      {action && <dd>{action}</dd>}
    </div>
  )
}

export { DetailList, Detail, type DetailListProps, type DetailProps, type DetailLayout }
