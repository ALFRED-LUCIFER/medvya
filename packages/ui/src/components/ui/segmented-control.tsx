import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "#lib/utils"

type Option<V extends string> = { value: V; label: React.ReactNode; icon?: LucideIcon }

type SegmentedControlProps<V extends string> = Omit<React.ComponentProps<"div">, "onChange"> & {
  /** Names the group for assistive technology, e.g. "View". */
  label: string
  options: readonly Option<V>[]
  value: V
  onValueChange: (value: V) => void
}

/**
 * The one view switch: a grey track with the selected segment in white. Each segment is a button with aria-pressed,
 * so Tab moves between them and Space or Enter selects.
 */
function SegmentedControl<V extends string>({ label, options, value, onValueChange, className, ...props }: SegmentedControlProps<V>) {
  return (
    <div role="group" aria-label={label} data-slot="segmented-control" className={cn("inline-flex w-fit gap-0.5 rounded-md bg-muted p-0.5", className)} {...props}>
      {options.map(({ value: v, label: text, icon: Icon }) => (
        <button
          key={v}
          type="button"
          aria-pressed={v === value}
          onClick={() => onValueChange(v)}
          className={cn(
            "inline-flex h-control items-center justify-center gap-2 rounded-md border border-transparent px-3 text-label text-muted-foreground transition-colors duration-fast ease-out",
            "hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
            "aria-pressed:border-border aria-pressed:bg-card aria-pressed:text-foreground"
          )}
        >
          {Icon && <Icon className="size-4" aria-hidden />}
          {text}
        </button>
      ))}
    </div>
  )
}

export { SegmentedControl, type SegmentedControlProps }
