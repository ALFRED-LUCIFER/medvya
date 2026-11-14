import * as React from "react"
import { XIcon } from "lucide-react"
import { Dialog as SheetPrimitive } from "radix-ui"
import { cn } from "#lib/utils"

// A side drawer (blueprint section 12): Radix Dialog, so focus is trapped, Escape closes and focus returns to the opener.
function Sheet(props: React.ComponentProps<typeof SheetPrimitive.Root>) {
  return <SheetPrimitive.Root data-slot="sheet" {...props} />
}

function SheetTrigger(props: React.ComponentProps<typeof SheetPrimitive.Trigger>) {
  return <SheetPrimitive.Trigger data-slot="sheet-trigger" {...props} />
}

function SheetContent({
  className,
  children,
  side = "end",
  closeLabel,
  onOpenAutoFocus,
  onCloseAutoFocus,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Content> & {
  /** Logical side: `end` is the right in English and the left in Arabic. */
  side?: "start" | "end"
  closeLabel: string
}) {
  // Radix returns focus only to a SheetTrigger; a sheet opened by state (SplitLayout) returns it to whatever opened it.
  const opener = React.useRef<HTMLElement | null>(null)
  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Overlay
        data-slot="sheet-overlay"
        className="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=closed]:animate-out data-[state=closed]:fade-out-0"
      />
      <SheetPrimitive.Content
        data-slot="sheet-content"
        data-side={side}
        // Radix defaults to ltr; follow the document so Arabic reads right to left inside the sheet.
        dir={document.documentElement.dir === "rtl" ? "rtl" : "ltr"}
        className={cn(
          // Translate 200 ms in, 150 ms out; under reduced motion only the fade remains (and the global rule makes that instant).
          "fixed inset-y-0 z-50 flex w-[calc(100%-3rem)] max-w-panel flex-col gap-3 overflow-y-auto bg-card p-4 shadow-dialog outline-none",
          "data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:duration-200 data-[state=open]:ease-[cubic-bezier(0.32,0.72,0,1)]",
          "data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:duration-fast data-[state=closed]:ease-out",
          side === "end"
            ? "end-0 border-s motion-safe:data-[state=open]:slide-in-from-end motion-safe:data-[state=closed]:slide-out-to-end"
            : "start-0 border-e motion-safe:data-[state=open]:slide-in-from-start motion-safe:data-[state=closed]:slide-out-to-start",
          className
        )}
        onOpenAutoFocus={e => { opener.current = document.activeElement as HTMLElement | null; onOpenAutoFocus?.(e) }}
        onCloseAutoFocus={e => {
          onCloseAutoFocus?.(e)
          if (e.defaultPrevented || !opener.current?.isConnected) return
          e.preventDefault()
          // The opener may sit in pinned chrome; returning focus must not move the page (the consultation keeps its place).
          opener.current.focus({ preventScroll: true })
        }}
        {...props}
      >
        {children}
        <SheetPrimitive.Close
          className="absolute end-2 top-2 inline-flex size-11 items-center justify-center rounded-md text-muted-foreground hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          aria-label={closeLabel}
        >
          <XIcon className="size-4" aria-hidden />
        </SheetPrimitive.Close>
      </SheetPrimitive.Content>
    </SheetPrimitive.Portal>
  )
}

function SheetTitle({ className, ...props }: React.ComponentProps<typeof SheetPrimitive.Title>) {
  return <SheetPrimitive.Title data-slot="sheet-title" className={cn("pe-12 text-heading", className)} {...props} />
}

function SheetDescription({ className, ...props }: React.ComponentProps<typeof SheetPrimitive.Description>) {
  return <SheetPrimitive.Description data-slot="sheet-description" className={cn("text-body text-muted-foreground", className)} {...props} />
}

export { Sheet, SheetTrigger, SheetContent, SheetTitle, SheetDescription }
