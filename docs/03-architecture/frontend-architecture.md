# Front-end architecture: domain-driven features, controller hooks (hard rule)

Applies to `web/clinic` and `web/portal`. Decided in [ADR-009](../adr/ADR-009-frontend-architecture.md). Enforced by lint (`web/clinic/.oxlintrc.json`): a violation fails `pnpm lint` and CI.

## Layers

```
src/
├── main.tsx                  # mounts <Root />
├── app/                      # composition only: providers, routing, wiring controllers → components
├── features/<feature>/       # one folder per domain capability (registration, booking, front-desk, encounter …)
│   ├── domain/               # pure TypeScript: types, value objects, rules (e.g. "PIN must be 6 digits")
│   ├── services/             # I/O only: API calls (generated client), storage, browser APIs
│   ├── hooks/                # controller hooks: useXxxController()
│   └── components/           # presentational React: props in, events out
└── dev/                      # developer-only pages (UI gallery)
```

Shared, feature-independent code lives in packages: `@medvya/ui` (design-system primitives), `@medvya/i18n` (translations and formatting), and later `@medvya/api-client` (generated from the API's OpenAPI document).

## What each layer may do

| Layer | Allowed | Forbidden (lint-enforced) |
|---|---|---|
| `domain/` | Types, pure functions, validation rules, Zod schemas | React, UI kit, services, hooks, components, `fetch`, `window`, `document`, storage |
| `services/` | HTTP via the API client, storage, browser APIs. Returns plain data | React, UI kit, hooks, components |
| `hooks/` (controllers) | React state and effects, **TanStack Query** (`useQuery`/`useMutation`), **React Hook Form**, calling services, applying domain rules. Returns a **view model + handlers** | UI (MUI, components), direct `fetch`/`localStorage` |
| `components/` | Rendering, layout, `useI18n()`, local UI-only state (open/closed, hover) | Services, controller hooks, TanStack, `fetch`, storage |
| `app/` | Calls controller hooks and passes their results to components; providers; router | Business rules (put them in `domain/`) |

## The controller hook pattern

One controller per screen or flow. It is the only place that knows where data comes from. The component never knows.

```ts
// features/registration/services/patients.ts — I/O only
export const searchDuplicates = (q: DuplicateQuery) => api.post('/v1/patients/duplicate-check', q);

// features/registration/domain/patient.ts — pure rules
export const pinCodeValid = (pin: string) => /^\d{6}$/.test(pin);

// features/registration/hooks/useRegistrationController.ts — orchestration
export function useRegistrationController() {
  const form = useForm<Registration>({ resolver: zodResolver(registrationSchema) });
  const duplicates = useQuery({ queryKey: ['dup', form.watch('name'), form.watch('dob')], queryFn: () => searchDuplicates(...) });
  const save = useMutation({ mutationFn: registerPatient });
  return { form, duplicates: duplicates.data ?? [], saving: save.isPending, onSubmit: form.handleSubmit(v => save.mutate(v)) };
}

// features/registration/components/RegistrationForm.tsx — presentational
export default function RegistrationForm(vm: ReturnType<typeof useRegistrationController>) { … }

// app/ (route element)
const vm = useRegistrationController();
return <RegistrationForm {...vm} />;
```

Why it matters for Medvya:

- **Testability:** domain rules and services are tested without React; controllers with `renderHook`; components with plain props.
- **Safety review:** every place that reads or writes patient data is a service, so security review covers a small, findable surface.
- **Portability:** the patient portal and a future mobile app (Expo) reuse `domain/` and `services/` unchanged.

## Libraries (PRD v2 §07, ADR-001)

| Concern | Library | When it lands |
|---|---|---|
| Server state, caching, retries | **TanStack Query** | First API data: `/v1/me/context` (MDV-P0-012) |
| Forms and validation | **React Hook Form + Zod** (Zod schemas in `domain/`) | In place: invite colleague form (MDV-P0-013); registration (MDV-P1-013) next |
| Routing | **React Router** (SPA) | App shell (MDV-P0-022) |
| API client | Generated from OpenAPI into `@medvya/api-client` | With the first endpoint used by the UI (MDV-P0-012) |
| UI | shadcn/ui + Radix + Tailwind 4 + Lucide via `@medvya/ui` (ADR-010) | In place |
| Tables | shadcn `Table` set in `@medvya/ui`, plain-TypeScript sort helper (`features/table/domain/tableSort.ts`). TanStack Table only when a screen needs column resizing, grouping or virtualised rows | In place (MDV-P0-030) |
| Dates | react-day-picker (shadcn calendar) | With the first date-picker screen (P1) |
| Schedule grid | Own CSS grid in `features/schedule` (FullCalendar's doctor and resource columns are a paid plugin, and we need our own slot buttons, states and RTL) | MDV-P1-011 |

## Tables (MDV-P0-030)

Never write `<table>`, `<tr>`, `<th>` or `<td>` in `features/` (lint rule `react/forbid-elements` fails the build). Use `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableHead`, `TableCell` and `TableState` from `@medvya/ui`.

- `domain/`: column definitions as plain data, and the sort values per column.
- `hooks/useXxxTableController`: sort, filter and paging state; returns rows, `sortOf(key)` and `onSort(key)`. Server data stays in TanStack Query. Sort and paging come from the shared `useTableController(rows, sortValues, columnLabel)` in `features/table` (MDV-P2-054): it also returns `status` (the sort, for `<Table status>`, announced politely) and `page`, `pageCount`, `onPage` for `<TablePagination>` (50 rows a page).
- `stacked` on `Table` turns rows into labelled blocks under 768 px (no sideways scroll on a phone); give each `TableCell` its column `label`. `density="compact"` (36 px rows, 48 px on a coarse pointer) is allowed on the audit log, formulary, order sets and staff tables only (lint rule `medvya-ui/compact-table`).
- `components/`: render the rows they are given. `TableState` shows the one loading, error and empty message; `TableHead` with `onSort` renders a 44 px button and sets `aria-sort`.
- Pass `label` to `Table` when it can scroll sideways, so the region is focusable and named. Use `numeric` for right-aligned (end-aligned, flips in Arabic) columns and `sticky` on `TableHeader` for long tables.

```tsx
const { rows, sortOf, onSort } = useStaffTableController(staff); // hook
<TableState state={rows.length ? 'ready' : 'empty'} messages={{ loading, error, empty }}>
  <Table label={t('staff.tableLabel')}>
    <TableHeader><TableRow>
      {staffColumns.map(c => <TableHead key={c.key} scope="col" sort={c.sortable ? sortOf(c.key) : undefined} onSort={c.sortable ? () => onSort(c.key) : undefined}>{t(`staff.col.${c.key}`)}</TableHead>)}
    </TableRow></TableHeader>
    <TableBody>{rows.map(m => <TableRow key={m.id}><TableCell>{m.name}</TableCell></TableRow>)}</TableBody>
  </Table>
</TableState>
```

## Navigation and route guards (MDV-P0-022)

`features/shell/domain/navigation.ts` is the single map of screen → path → required capability (and access levels) → optional tenant feature flag → delivery phase. The shell renders, and the router registers, only the items `visibleNav()` returns for the active membership from `/v1/me/context`, so a hidden screen has no route (it shows "Page not found"). Screens after the R1 launch carry a flag and stay hidden until the tenant turns it on. This is a UX filter only: every API endpoint still enforces its own policy (AGENTS.md 1.3).

No other state library (Redux, Zustand…) unless a decision record justifies it. Server state belongs in TanStack Query, form state in React Hook Form, and the little UI state left in `useState`.
