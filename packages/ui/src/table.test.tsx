// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render } from '@testing-library/react';
import axe from 'axe-core';
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow, TableState } from './components/ui/table';

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
