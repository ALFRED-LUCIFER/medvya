import * as React from "react"
import { cn } from "#lib/utils"

/** The only two card sizes (blueprint section 7): a page section, and an item in a list. */
const cardSizes = {
  section: "gap-3 rounded-lg p-4",
  item: "gap-2 rounded-md p-3",
} as const

type Slots = {
  title?: React.ReactNode
  /** One or two sentences under the title. */
  intro?: React.ReactNode
  /** Buttons at the end of the title row. */
  actions?: React.ReactNode
}

function Head({ title, intro, actions, level }: Slots & { level: 2 | 3 }) {
  if (!title && !intro && !actions) return null
  const H = level === 2 ? "h2" : "h3"
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="flex min-w-0 flex-col gap-1">
        {title && <H className={level === 2 ? "text-heading" : "text-subheading"}>{title}</H>}
        {intro && <p className="text-body text-muted-foreground">{intro}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

const InCard = React.createContext(false)
// Vite and Vitest replace process.env.NODE_ENV; declared here so apps without Node types still compile.
declare const process: { env: { NODE_ENV?: string } }

type CardProps = Omit<React.ComponentProps<"section">, "title"> & Slots & { size?: keyof typeof cardSizes }

/** A bordered surface. A section card titles with h2, a list-item card with h3. Never put a Card directly inside a Card. */
function Card({ size = "section", title, intro, actions, className, children, ...props }: CardProps) {
  const nested = React.useContext(InCard)
  // A bordered card in a bordered card is the pattern this component replaces (F-19); say so while developing.
  if (nested && process.env.NODE_ENV !== "production") console.error("Card: a bordered Card is nested in another Card. Use Section inside a Card.")
  return (
    <section
      data-slot="card"
      data-size={size}
      data-nested={nested || undefined}
      className={cn("flex min-w-0 flex-col border bg-card text-card-foreground", cardSizes[size], className)}
      {...props}
    >
      <Head title={title} intro={intro} actions={actions} level={size === "section" ? 2 : 3} />
      <InCard.Provider value>{children}</InCard.Provider>
    </section>
  )
}

/** An unbordered group with the same title, intro and actions slots; use it inside a Card, or for a page section without a surface. */
function Section({ title, intro, actions, className, children, ...props }: Omit<React.ComponentProps<"section">, "title"> & Slots) {
  const inCard = React.useContext(InCard)
  return (
    <section data-slot="section" className={cn("flex min-w-0 flex-col gap-3", className)} {...props}>
      <Head title={title} intro={intro} actions={actions} level={inCard ? 3 : 2} />
      {children}
    </section>
  )
}

export { Card, Section, type CardProps }
