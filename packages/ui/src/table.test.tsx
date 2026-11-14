// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render } from '@testing-library/react';
import axe from 'axe-core';
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState } from './components/ui/table';

const sample = (extra?: { sort?: 'ascending' | 'descending' | 'none'; onSort?: () => void }) => (
  <Table label="People">
    <TableCaption>Who is here</TableCaption>
    <TableHeader sticky>
      <TableRow>
        <TableHead scope="col" {...extra}>Name</TableHead>
        <TableHead scope="col" numeric>Visits</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow data-state="selected">
        <TableHead scope="row">Asha</TableHead>
        <TableCell numeric>3</TableCell>
      </TableRow>
    </TableBody>
  </Table>
);

describe('Table', () => {
  it('renders real table semantics inside a labelled, focusable scroll region', () => {
    const { container, getByRole } = render(sample());
    expect(getByRole('region', { name: 'People' }).tabIndex).toBe(0);
    expect(getByRole('table', { name: 'Who is here' })).toBeTruthy();
    expect(container.querySelector('caption')?.textContent).toBe('Who is here');
    expect(container.querySelectorAll('th[scope="col"]')).toHaveLength(2);
    expect(container.querySelector('th[scope="row"]')?.textContent).toBe('Asha');
  });

  it('uses logical alignment, a sticky header option and 44 px cells', () => {
    const { container } = render(sample());
    expect(container.querySelector('thead')?.className).toContain('sticky');
    expect(container.querySelectorAll('th')[1]?.className).toContain('text-end');
    expect(container.querySelector('td')?.className).toContain('text-end');
    expect(container.querySelector('td')?.className).toContain('min-h-11');
    expect(container.querySelector('th')?.className).toContain('h-11');
    expect(container.innerHTML).not.toMatch(/text-left|text-right|\bpl-|\bpr-|\bml-|\bmr-/);
  });

  it('sortable header is a button that sets aria-sort and calls onSort', () => {
    const onSort = vi.fn();
    const { container, getByRole, rerender } = render(sample({ sort: 'ascending', onSort }));
    expect(container.querySelector('th')?.getAttribute('aria-sort')).toBe('ascending');
    fireEvent.click(getByRole('button', { name: 'Name' }));
    expect(onSort).toHaveBeenCalledOnce();
    rerender(sample({ onSort }));
    expect(container.querySelector('th')?.getAttribute('aria-sort')).toBe('none');
  });

  it('plain headers carry no aria-sort and no button', () => {
    const { container } = render(sample());
    expect(container.querySelector('[aria-sort]')).toBeNull();
    expect(container.querySelector('button')).toBeNull();
  });

  it('marks a selected row by more than colour', () => {
    const { container } = render(sample());
    expect(container.querySelector('tr[data-state="selected"]')?.className).toContain('border-s-4');
  });

  it('has no axe violations', async () => {
    const { container } = render(sample({ sort: 'descending', onSort: () => {} }));
    expect((await axe.run(container)).violations).toEqual([]);
  });
});

// MDV-P2-054: compact density, stacked rows and paging.
const layout = (props: { density?: 'compact'; stacked?: boolean; status?: string }) => (
  <Table label="Medicines" {...props}>
    <TableHeader><TableRow><TableHead scope="col">Name</TableHead><TableHead scope="col" numeric>Strength</TableHead></TableRow></TableHeader>
    <TableBody><TableRow><TableHead scope="row">Metformin</TableHead><TableCell numeric label="Strength">500</TableCell></TableRow></TableBody>
  </Table>
);

describe('Table layout', () => {
  afterEach(cleanup);
  it('is comfortable by default: no compact rows, no stacked roles, no cell label', () => {
    const { container } = render(layout({}));
    expect(container.querySelector('[data-slot=table-container]')?.getAttribute('data-density')).toBe('comfortable');
    expect(container.querySelector('tbody')?.className).not.toContain('h-row-compact');
    expect(container.querySelector('tbody')?.className).toContain('[&>tr]:h-row'); // 48 px rows, text-only ones too (DESIGN.md)
    expect(container.querySelector('[role=row], [role=cell]')).toBeNull();
    expect(container.querySelector('td')?.textContent).toBe('500');
  });

  it('compact rows are 36 px with a mouse and 48 px on any coarse pointer', () => {
    const { container } = render(layout({ density: 'compact' }));
    const body = container.querySelector('tbody')!.className;
    expect(body).toContain('[&>tr]:h-row-compact');
    expect(body).toContain('any-pointer-coarse:[&>tr]:h-row');
    // A row's button is 32 px with a mouse, never under 44 px on a coarse pointer.
    expect(body).toContain('[&_button]:min-h-8');
    expect(body).toContain('any-pointer-coarse:[&_button]:min-h-11');
    expect(container.querySelector('[data-slot=table-container]')?.getAttribute('data-density')).toBe('compact');
  });

  it('stacked: roles survive display:block, the header is only for screen readers on a phone, each cell shows its column', () => {
    const { container, getByRole } = render(layout({ density: 'compact', stacked: true }));
    expect(getByRole('table').getAttribute('role')).toBe('table');
    expect(container.querySelector('thead')?.className).toContain('max-md:sr-only');
    expect(getByRole('rowheader', { name: 'Metformin' })).toBeTruthy();
    expect(getByRole('cell').querySelector('[aria-hidden]')?.textContent).toBe('Strength');
    // Compact applies from 768 px up only; a stacked phone row sizes to its content.
    expect(container.querySelector('tbody')?.className).toContain('md:[&>tr]:h-row-compact');
    expect(container.querySelector('tbody')?.className).not.toMatch(/(^|\s)\[&>tr\]:h-row-compact/);
  });

  it('a comfortable stacked row is 48 px from 768 px up and sizes to its content on a phone', () => {
    const body = render(layout({ stacked: true })).container.querySelector('tbody')?.className ?? '';
    expect(body).toContain('md:[&>tr]:h-row');
    expect(body).not.toMatch(/(^|\s)\[&>tr\]:h-row/);
  });

  it('announces the status politely, and is axe clean stacked', async () => {
    const { container, getByRole } = render(layout({ stacked: true, status: 'Sorted by Name, ascending' }));
    expect(getByRole('status').textContent).toBe('Sorted by Name, ascending');
    expect((await axe.run(container)).violations).toEqual([]);
  });
});

describe('TablePagination', () => {
  afterEach(cleanup);
  it('shows nothing for one page, and previous/next that stop at the ends', () => {
    const onPage = vi.fn();
    expect(render(<TablePagination page={0} pageCount={1} onPage={onPage} />).container.innerHTML).toBe('');
    const { getByRole, getByText, rerender } = render(<TablePagination page={0} pageCount={3} onPage={onPage} />);
    expect(getByRole('navigation', { name: 'Pages' })).toBeTruthy();
    expect(getByText('Page 1 of 3')).toBeTruthy();
    expect((getByRole('button', { name: 'Previous page' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(getByRole('button', { name: 'Next page' }));
    expect(onPage).toHaveBeenLastCalledWith(1);
    rerender(<TablePagination page={2} pageCount={3} onPage={onPage} />);
    expect((getByRole('button', { name: 'Next page' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(getByRole('button', { name: 'Previous page' }));
    expect(onPage).toHaveBeenLastCalledWith(1);
  });
});

describe('TableState', () => {
  const messages = { loading: 'Loading', error: 'Failed', empty: 'None' };
  it('shows each state once and the table only when ready', () => {
    const roles = (s: 'loading' | 'error' | 'empty' | 'ready') => {
      const { container } = render(<TableState state={s} messages={messages}><table /></TableState>);
      return [container.querySelector('[role="status"]')?.textContent, container.querySelector('[role="alert"]')?.textContent, !!container.querySelector('table')];
    };
    expect(roles('loading')).toEqual(['Loading', undefined, false]);
    expect(roles('error')).toEqual([undefined, 'Failed', false]);
    expect(roles('empty')).toEqual(['None', undefined, false]);
    expect(roles('ready')).toEqual([undefined, undefined, true]);
  });
});
