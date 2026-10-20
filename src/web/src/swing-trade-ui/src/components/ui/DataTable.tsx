import { Children, isValidElement, useMemo, useState } from "react";
import {
  Cell,
  Column as AriaColumn,
  Row,
  Table,
  TableBody,
  TableHeader,
  type SortDescriptor,
} from "react-aria-components";
import { faSort, faSortDown, faSortUp } from "@fortawesome/free-solid-svg-icons";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";

export interface ColumnProps<T> {
  field?: string;
  header?: React.ReactNode;
  body?: (row: T) => React.ReactNode;
  sortable?: boolean;
  align?: "left" | "right";
  /** Keeps the column in view while the table scrolls sideways. */
  frozen?: boolean;
  alignFrozen?: "left" | "right";
  style?: React.CSSProperties;
}

// Configuration-only element: DataTable reads its props and renders the cells itself.
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export const Column = <T,>(_props: ColumnProps<T>): null => null;

interface DataTableProps<T> {
  value: T[];
  /** The row field that identifies it; defaults to "id". */
  dataKey?: string;
  emptyMessage?: React.ReactNode;
  stripedRows?: boolean;
  rowClassName?: (row: T) => string;
  onRowClick?: (row: T) => void;
  "aria-label": string;
  children?: React.ReactNode;
}

const getField = (row: unknown, path: string): unknown =>
  path.split(".").reduce<unknown>((acc, key) => (acc == null ? undefined : (acc as Record<string, unknown>)[key]), row);

const toText = (value: unknown): string => {
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  if (value instanceof Date) return value.toISOString();
  return JSON.stringify(value) ?? "";
};

const compareValues = (a: unknown, b: unknown): number => {
  if (a == null && b == null) return 0;
  if (a == null) return 1;
  if (b == null) return -1;
  const na = typeof a === "number" ? a : Number(a);
  const nb = typeof b === "number" ? b : Number(b);
  if (Number.isFinite(na) && Number.isFinite(nb)) return na - nb;
  return toText(a).localeCompare(toText(b));
};

const colId = (column: ColumnProps<unknown>, index: number) => column.field ?? `col-${index}`;

const SORT_ICONS = { ascending: faSortUp, descending: faSortDown } as const;

const SortIcon: React.FC<{ direction?: "ascending" | "descending" }> = ({ direction }) => (
  <FontAwesomeIcon
    icon={direction ? SORT_ICONS[direction] : faSort}
    className={direction ? "text-brand" : "text-content-muted"}
    aria-hidden
  />
);

const frozenClass = (column: ColumnProps<unknown>) =>
  column.frozen ? `sticky z-[1] ${column.alignFrozen === "right" ? "right-0" : "left-0"}` : "";

export function DataTable<T>({
  value,
  dataKey = "id",
  emptyMessage = "No records found.",
  stripedRows,
  rowClassName,
  onRowClick,
  "aria-label": ariaLabel,
  children,
}: Readonly<DataTableProps<T>>) {
  const columns = Children.toArray(children)
    .filter(isValidElement)
    .map((el) => el.props as ColumnProps<T>);

  const [sort, setSort] = useState<SortDescriptor | undefined>();

  const sorted = useMemo(() => {
    if (!sort) return value;
    const sign = sort.direction === "descending" ? -1 : 1;
    const field = String(sort.column);
    return [...value].sort((a, b) => sign * compareValues(getField(a, field), getField(b, field)));
  }, [value, sort]);

  // Ascending, descending, then back to the source order.
  const changeSort = (next: SortDescriptor) => {
    const clearing = sort?.column === next.column && sort.direction === "descending";
    setSort(clearing ? undefined : next);
  };

  const keyOf = (row: T, index: number): string | number => {
    const id = getField(row, dataKey);
    return typeof id === "string" || typeof id === "number" ? id : index;
  };

  return (
    <div className="w-full overflow-x-auto">
      <Table
        aria-label={ariaLabel}
        sortDescriptor={sort}
        onSortChange={changeSort}
        className="w-full border-collapse text-left text-sm text-content"
      >
        <TableHeader>
          {columns.map((column, i) => (
            <AriaColumn
              key={colId(column as ColumnProps<unknown>, i)}
              id={colId(column as ColumnProps<unknown>, i)}
              isRowHeader={i === 0}
              allowsSorting={column.sortable}
              style={column.style}
              className={[
                "border-b border-border-strong bg-surface-muted px-3 py-2 font-semibold whitespace-nowrap outline-none",
                "data-focus-visible:outline-2 data-focus-visible:-outline-offset-2 data-focus-visible:outline-brand",
                column.align === "right" ? "text-right" : "",
                frozenClass(column as ColumnProps<unknown>),
              ].join(" ")}
            >
              {({ sortDirection }) => (
                <span
                  className={`inline-flex items-center gap-2 ${column.align === "right" ? "flex-row-reverse" : ""}`}
                >
                  {column.header}
                  {column.sortable && <SortIcon direction={sortDirection} />}
                </span>
              )}
            </AriaColumn>
          ))}
        </TableHeader>
        <TableBody
          renderEmptyState={() => <div className="px-3 py-6 text-center text-content-muted">{emptyMessage}</div>}
        >
          {sorted.map((row, rowIndex) => {
            const extra = rowClassName?.(row);
            const background = extra || (stripedRows && rowIndex % 2 === 1 ? "bg-surface-muted" : "bg-surface");
            return (
              <Row
                key={keyOf(row, rowIndex)}
                id={keyOf(row, rowIndex)}
                columns={columns}
                onAction={onRowClick ? () => onRowClick(row) : undefined}
                className={[background, onRowClick ? "cursor-pointer" : ""].join(" ")}
              >
                {columns.map((column, i) => (
                  <Cell
                    key={colId(column as ColumnProps<unknown>, i)}
                    style={column.style}
                    className={[
                      "border-b border-border px-3 py-2 outline-none",
                      column.align === "right" ? "text-right" : "",
                      column.frozen ? `bg-inherit ${frozenClass(column as ColumnProps<unknown>)}` : "",
                    ].join(" ")}
                  >
                    {column.body ? column.body(row) : (getField(row, column.field ?? "") as React.ReactNode)}
                  </Cell>
                ))}
              </Row>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
