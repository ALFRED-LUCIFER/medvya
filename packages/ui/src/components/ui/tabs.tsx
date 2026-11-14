import * as React from "react"
import { cn } from "#lib/utils"
import { Tabs as TabsPrimitive } from "radix-ui"

function Tabs({
  className,
  orientation = "horizontal",
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      data-orientation={orientation}
      orientation={orientation}
      // Radix defaults to ltr, which flipped everything inside a tab (tables included) back to left-to-right in Arabic.
      dir={document.documentElement.dir === 'rtl' ? 'rtl' : 'ltr'}
      className={cn(
        "group/tabs flex gap-2 data-[orientation=horizontal]:flex-col",
        className
      )}
      {...props}
    />
  )
}

// Tabs are text with a 2 px underline (workspace concept and Medvya Web .tab), not filled boxes (MDV-P2-062). Radix sets
// aria-selected and data-orientation on each tab (its own, so nested tabs never mix); the chosen tab is also bold and coloured, so the state never rests on the underline's colour alone.
function TabsList({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      className={cn(
        "flex flex-wrap gap-x-1 text-muted-foreground data-[orientation=horizontal]:border-b data-[orientation=vertical]:flex-col data-[orientation=vertical]:border-s",
        className
      )}
      {...props}
    />
  )
}

function TabsTrigger({ className, ...props }: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        "inline-flex min-h-control items-center gap-1.5 border-transparent px-2.5 text-body font-semibold whitespace-nowrap text-muted-foreground transition-[color,border-color] duration-fast ease-out hover:text-foreground disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        "data-[orientation=horizontal]:-mb-px data-[orientation=horizontal]:border-b-2 data-[orientation=vertical]:-ms-px data-[orientation=vertical]:border-s-2 data-[orientation=vertical]:text-start",
        "aria-selected:border-brand-700 aria-selected:font-bold aria-selected:text-brand-700",
        className
      )}
      {...props}
    />
  )
}

function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    // Radix makes the panel a tab stop; it keeps the global focus ring (styles.css) so a keyboard user sees where focus went.
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("flex-1", className)}
      {...props}
    />
  )
}

export { Tabs, TabsList, TabsTrigger, TabsContent }
