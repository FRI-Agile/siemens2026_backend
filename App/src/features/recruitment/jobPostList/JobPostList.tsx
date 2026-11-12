import {
  Button,
  Checkbox,
  CounterBadge,
  Field,
  Input,
  Label,
  makeStyles,
  mergeClasses,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Radio,
  RadioGroup,
  useId,
} from '@fluentui/react-components'
import {
  ChevronLeftRegular,
  ChevronRightRegular,
  FilterRegular,
  SearchRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import {
  employmentTypeLabels,
  formatSalary,
  type EmploymentType,
  type PaymentType,
} from '../vocabulary.ts'

// The job post list both the public page and the staff page show: the same heading, search, filter menu, table and
// pager, so the two stay alike. Each page adds its own columns, filters and data.

const useStyles = makeStyles({
  main: {
    // index.css centres every <main>'s children (`display: grid; place-items: center`), and browsers now apply
    // justify-items to block layout too, so both are reset: the list is a left-aligned column.
    display: 'block',
    justifyItems: 'stretch',
    width: '100%',
    maxWidth: '1120px',
    boxSizing: 'border-box',
    margin: '0 auto',
    padding: '48px 32px 72px',
    '@media (max-width: 700px)': {
      padding: '36px 16px 56px',
    },
  },
  hero: {
    paddingTop: '64px',
    paddingBottom: '88px',
  },
  header: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: '16px 24px',
    marginBottom: '20px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '32px',
    lineHeight: 1.2,
  },
  heroHeading: {
    fontSize: 'clamp(32px, 5vw, 48px)',
    lineHeight: 1.15,
  },
  intro: {
    maxWidth: '650px',
    margin: '12px 0 0',
    color: '#475569',
    fontSize: '16px',
    lineHeight: 1.6,
  },
  heroIntro: {
    fontSize: '17px',
  },
  toolbar: {
    // Stays in view under the layout's sticky header while the list scrolls; the layout sets both variables.
    position: 'sticky',
    top: 'var(--app-header-height, 0px)',
    zIndex: 1,
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    margin: '0 0 12px',
    padding: '12px 0',
    backgroundColor: 'var(--app-background, #F1F3F5)',
  },
  search: {
    flex: '1 1 auto',
    minWidth: 0,
  },
  filterButton: {
    flexShrink: 0,
  },
  filterSurface: {
    display: 'grid',
    gap: '20px',
    width: '320px',
    maxWidth: 'calc(100vw - 32px)',
    boxSizing: 'border-box',
    padding: '20px',
    // autoSize caps the height to the room below the button; the rest scrolls inside.
    overflowY: 'auto',
  },
  filterLabel: {
    display: 'block',
    paddingBottom: '4px',
  },
  filterChecks: {
    display: 'grid',
  },
  filterActions: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
  tableWrapper: {
    overflowX: 'auto',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    backgroundColor: '#FFFFFF',
    boxShadow: '0 4px 16px rgba(31, 41, 51, 0.06)',
  },
  table: {
    width: '100%',
    minWidth: '760px',
    borderCollapse: 'collapse',
  },
  headerCell: {
    padding: '16px 20px',
    color: '#475569',
    fontSize: '13px',
    fontWeight: 700,
    textAlign: 'left',
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    backgroundColor: '#F7F8FA',
    borderBottom: '1px solid #E5E7EB',
  },
  row: {
    ':last-child td': {
      borderBottom: 'none',
    },
    ':hover': {
      backgroundColor: '#FFF7F1',
    },
  },
  cell: {
    padding: '18px 20px',
    color: '#475569',
    textAlign: 'left',
    borderBottom: '1px solid #E5E7EB',
  },
  title: {
    color: '#1F2933',
    fontWeight: 700,
    textDecoration: 'none',
    ':hover': {
      color: '#D8631D',
      textDecoration: 'underline',
    },
  },
  message: {
    padding: '36px 20px',
    color: '#687280',
    textAlign: 'center',
  },
  pager: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    marginTop: '20px',
    color: '#475569',
  },
  pagerButtons: {
    display: 'flex',
    gap: '8px',
  },
})

/** The page around a job post list: its heading, an introduction and an optional action beside them. */
export function JobPostListPage({
  heading,
  intro,
  action,
  hero = false,
  children,
}: {
  heading: string
  intro: string
  action?: ReactNode
  /** The public page's larger heading. */
  hero?: boolean
  children: ReactNode
}) {
  const styles = useStyles()
  return (
    <main className={mergeClasses(styles.main, hero && styles.hero)}>
      <div className={styles.header}>
        <div>
          <h1
            className={mergeClasses(styles.heading, hero && styles.heroHeading)}
          >
            {heading}
          </h1>
          <p className={mergeClasses(styles.intro, hero && styles.heroIntro)}>
            {intro}
          </p>
        </div>
        {action}
      </div>
      {children}
    </main>
  )
}

/** The search box and the filter menu beside it; `children` are the menu's sections. */
export function JobPostListToolbar({
  searchInput,
  onSearchInput,
  activeFilters,
  onClearFilters,
  children,
}: {
  searchInput: string
  onSearchInput: (value: string) => void
  /** How many filters differ from the default; shown on the filter button. */
  activeFilters: number
  onClearFilters: () => void
  children: ReactNode
}) {
  const styles = useStyles()
  return (
    <div className={styles.toolbar}>
      <Input
        className={styles.search}
        size="large"
        contentBefore={<SearchRegular aria-hidden="true" />}
        value={searchInput}
        maxLength={200}
        onChange={(event) => onSearchInput(event.target.value)}
        placeholder="Hľadať podľa názvu alebo lokality"
        aria-label="Hľadať pracovné pozície"
        type="search"
      />
      {/* Pinned below the button and sized to the room there, so a long menu scrolls rather than flipping or sliding
          up over the sticky header. */}
      <Popover
        positioning={{
          position: 'below',
          align: 'end',
          pinned: true,
          autoSize: 'height',
          overflowBoundaryPadding: 8,
        }}
        trapFocus
      >
        <PopoverTrigger disableButtonEnhancement>
          <Button
            className={styles.filterButton}
            size="large"
            icon={<FilterRegular />}
            aria-label={
              activeFilters > 0
                ? `Filtre a zoradenie, ${activeFilters} aktívne`
                : 'Filtre a zoradenie'
            }
          >
            Filtre
            {activeFilters > 0 && (
              <CounterBadge
                count={activeFilters}
                size="small"
                color="brand"
                style={{ marginLeft: 8 }}
              />
            )}
          </Button>
        </PopoverTrigger>
        <PopoverSurface
          className={styles.filterSurface}
          aria-label="Filtre a zoradenie"
        >
          {children}
          <div className={styles.filterActions}>
            <Button
              appearance="subtle"
              disabled={activeFilters === 0}
              onClick={onClearFilters}
            >
              Zrušiť filtre
            </Button>
          </div>
        </PopoverSurface>
      </Popover>
    </div>
  )
}

/** One section of the filter menu: a choice of one value. */
export function FilterChoice<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: readonly { value: T; label: string }[]
  onChange: (value: T) => void
}) {
  return (
    <Field label={label}>
      <RadioGroup
        value={value}
        onChange={(_, data) => onChange(data.value as T)}
      >
        {options.map((option) => (
          <Radio key={option.value} value={option.value} label={option.label} />
        ))}
      </RadioGroup>
    </Field>
  )
}

/** One section of the filter menu: any number of values, where none ticked means no filter. */
export function FilterChecks<T extends string>({
  label,
  values,
  options,
  onToggle,
}: {
  label: string
  values: readonly T[]
  options: readonly { value: T; label: string }[]
  onToggle: (value: T) => void
}) {
  const styles = useStyles()
  const labelId = useId('filter-checks')
  return (
    <div role="group" aria-labelledby={labelId}>
      <Label id={labelId} className={styles.filterLabel}>
        {label}
      </Label>
      <div className={styles.filterChecks}>
        {options.map((option) => (
          <Checkbox
            key={option.value}
            label={option.label}
            checked={values.includes(option.value)}
            onChange={() => onToggle(option.value)}
          />
        ))}
      </div>
    </div>
  )
}

/** What every row of a job post list shows. */
export interface JobPostRow {
  id: string
  title: string
  location: string
  employmentType: EmploymentType
  salary: number
  paymentType: PaymentType
}

/** The job post table; a `message` (loading, an error, nothing found) shows in place of the rows. */
export function JobPostTable<T extends JobPostRow>({
  items,
  linkTo,
  extraColumn,
  message,
  busy,
}: {
  items: readonly T[]
  linkTo: (post: T) => string
  /** A last column one page adds, e.g. the staff page's status. */
  extraColumn?: { header: string; render: (post: T) => ReactNode }
  message?: string
  busy: boolean
}) {
  const styles = useStyles()
  return (
    <div className={styles.tableWrapper}>
      <table className={styles.table} aria-busy={busy}>
        <thead>
          <tr>
            <th className={styles.headerCell}>Názov pozície</th>
            <th className={styles.headerCell}>Lokalita</th>
            <th className={styles.headerCell}>Typ pracovného pomeru</th>
            <th className={styles.headerCell}>Mzda</th>
            {extraColumn && (
              <th className={styles.headerCell}>{extraColumn.header}</th>
            )}
          </tr>
        </thead>
        <tbody>
          {message ? (
            <tr>
              <td className={styles.message} colSpan={extraColumn ? 5 : 4}>
                {message}
              </td>
            </tr>
          ) : (
            items.map((post) => (
              <tr className={styles.row} key={post.id}>
                <td className={styles.cell}>
                  <Link className={styles.title} to={linkTo(post)}>
                    {post.title}
                  </Link>
                </td>
                <td className={styles.cell}>{post.location}</td>
                <td className={styles.cell}>
                  {employmentTypeLabels[post.employmentType]}
                </td>
                <td className={styles.cell}>
                  {formatSalary(post.salary, post.paymentType)}
                </td>
                {extraColumn && (
                  <td className={styles.cell}>{extraColumn.render(post)}</td>
                )}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  )
}

/** "Showing 1–20 of 45" and the previous and next page buttons; nothing while the page is empty. */
export function JobPostPager({
  page,
  offset,
  shown,
  total,
  disabled,
  onPage,
}: {
  page: number
  offset: number
  /** How many posts this page shows. */
  shown: number
  total: number
  disabled: boolean
  onPage: (page: number) => void
}) {
  const styles = useStyles()
  if (shown === 0) return null
  const last = offset + shown
  return (
    <div className={styles.pager}>
      <span>
        Zobrazené {offset + 1}–{last} z {total}
      </span>
      <div className={styles.pagerButtons}>
        <Button
          icon={<ChevronLeftRegular />}
          disabled={page <= 1 || disabled}
          onClick={() => onPage(page - 1)}
        >
          Predchádzajúca
        </Button>
        <Button
          icon={<ChevronRightRegular />}
          iconPosition="after"
          disabled={last >= total || disabled}
          onClick={() => onPage(page + 1)}
        >
          Ďalšia
        </Button>
      </div>
    </div>
  )
}
