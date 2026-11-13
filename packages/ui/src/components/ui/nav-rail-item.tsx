import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { Slot } from "radix-ui"
import { cn } from "#lib/utils"

type NavRailItemProps = React.ComponentProps<"a"> & {
  icon: LucideIcon
  /** Always visible under the icon; never a tooltip only (ruling 8). */
  label: React.ReactNode
  /** Pass the link element (e.g. a router NavLink) as the only child; the icon and label are rendered inside it. */
  asChild?: boolean
}

/**
 * One item of the 72 px navigation rail on the navy surface: icon with its label under it, wrapping rather than truncating.
 * The current page is marked with aria-current="page" (a router NavLink sets it), which is also what styles it.
 */
function NavRailItem({ icon: Icon, label, asChild, className, children, ...props }: NavRailItemProps) {
  const Comp = asChild ? Slot.Root : "a"
  return (
    <Comp
      data-slot="nav-rail-item"
      className={cn(
        "flex min-h-14 w-full flex-col items-center justify-center gap-1 rounded-lg px-1 py-1.5 text-center text-nav-ink transition-colors duration-fast ease-out",
        "hover:bg-nav-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white aria-[current=page]:bg-nav-hover aria-[current=page]:text-white",
        className
      )}
      {...props}
    >
      <Icon className="size-5 shrink-0" aria-hidden />
      {asChild ? <Slot.Slottable>{children}</Slot.Slottable> : null}
      {/* The rail label is DESIGN.md's one named exception to the type scale: 11 px. */}
      <span className="text-[11px] leading-[14px] font-semibold wrap-anywhere">{label}</span>
    </Comp>
  )
}

export { NavRailItem, type NavRailItemProps }
