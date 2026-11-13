import * as React from "react"
import { cn } from "#lib/utils"

/** A native radio or checkbox whose whole 44 px row is the target. `className` styles the row; the other props go to the input. */
function ChoiceRow({
  className,
  children,
  ...props
}: Omit<React.ComponentProps<"input">, "type"> & { type: "radio" | "checkbox" }) {
  return (
    <label
      data-slot="choice-row"
      className={cn("flex min-h-11 cursor-pointer items-center gap-2 text-sm has-disabled:cursor-not-allowed", className)}
    >
      <input className="size-4 shrink-0 accent-brand-700" {...props} />
      {children}
    </label>
  )
}

export { ChoiceRow }
