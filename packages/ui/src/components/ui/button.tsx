import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { Loader2 } from "lucide-react"
import { Slot } from "radix-ui"
import { cn } from "#lib/utils"

// Medvya buttons (Handoff §1): 44px minimum target, bold label, disabled shows a grey fill rather than opacity.
// Focus uses the global 2px ring from styles.css.
const buttonVariants = cva(
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-md text-sm font-bold whitespace-nowrap transition-colors disabled:pointer-events-none disabled:bg-[#e6ecf1] disabled:text-ink-400 disabled:border-transparent [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-brand-800",
        outline: "border border-input bg-card text-foreground hover:bg-accent",
        ai: "bg-ai-600 text-white hover:bg-ai-700",
        destructive: "bg-destructive text-destructive-foreground hover:bg-[#8a1626]",
        navy: "bg-brand-950 text-white hover:bg-brand-900",
        ghost: "text-foreground hover:bg-accent",
        link: "text-action-600 underline-offset-4 hover:underline",
      },
      size: {
        default: "min-h-11 px-4 has-[>svg]:px-3",
        sm: "min-h-9 px-3 text-[13px]",
        icon: "size-11",
      },
    },
    defaultVariants: { variant: "default", size: "default" },
  }
)

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  loading = false,
  disabled,
  children,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
    /** Shows a spinner, disables the button and sets aria-busy. */
    loading?: boolean
  }) {
  const Comp = asChild ? Slot.Root : "button"
  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      className={cn(buttonVariants({ variant, size, className }))}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading && <Loader2 className="animate-spin" aria-hidden />}
      {children}
    </Comp>
  )
}

export { Button, buttonVariants }
