import {
  Button,
  Input,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Select,
} from '@fluentui/react-components'
import { AddRegular, SearchRegular } from '@fluentui/react-icons'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import {
  useManagedJobPosts,
  type ManagedJobPostStatus,
} from '../../../features/recruitment/managedJobPosts.ts'
import {
  employmentTypeLabels,
  employmentTypes,
} from '../../../features/recruitment/vocabulary.ts'
import { ApiError } from '../../../shared/api/http.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

const useStyles = makeStyles({
  main: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '48px 32px',
  },
  headingRow: {
    display: 'flex',
    alignItems: 'start',
    justifyContent: 'space-between',
    gap: '20px',
    marginBottom: '12px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '32px',
  },
  subtitle: {
    margin: '12px 0 32px',
    color: '#475569',
  },
  filters: {
    display: 'grid',
    gridTemplateColumns: 'minmax(240px, 2fr) minmax(180px, 1fr)',
    gap: '16px',
    marginBottom: '24px',
  },
  filter: {
    width: '100%',
  },
  search: {
    display: 'flex',
    alignItems: 'center',
    gap: '10px',
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
  cell: {
    padding: '18px 20px',
    color: '#475569',
    textAlign: 'left',
    borderBottom: '1px solid #E5E7EB',
  },
  headerCell: {
    padding: '16px 20px',
    color: '#475569',
    fontSize: '13px',
    fontWeight: 700,
    textAlign: 'left',
    textTransform: 'uppercase',
    backgroundColor: '#F7F8FA',
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
  status: {
    display: 'inline-block',
    padding: '4px 10px',
    borderRadius: '999px',
    fontSize: '13px',
    fontWeight: 700,
  },
  draft: {
    color: '#6B4E00',
    backgroundColor: '#FFF4CC',
  },
  published: {
    color: '#146C43',
    backgroundColor: '#D9F5E5',
  },
  closed: {
    color: '#5B6470',
    backgroundColor: '#E8EBEF',
  },
  error: {
    marginBottom: '20px',
  },
  empty: {
    padding: '36px 20px',
    color: '#687280',
    textAlign: 'center',
  },
  '@media (max-width: 700px)': {
    main: {
      padding: '36px 24px',
    },
    headingRow: {
      display: 'grid',
    },
    filters: {
      gridTemplateColumns: '1fr',
    },
  },
})

const statusLabels: Record<ManagedJobPostStatus, string> = {
  draft: 'Koncept',
  published: 'Zverejnená',
  closed: 'Uzavretá',
}

export function StaffJobPostsPage() {
  const styles = useStyles()
  const { data, error, isPending } = useManagedJobPosts()
  const { data: me } = useCurrentStaff()
  const [search, setSearch] = useState('')
  const [employmentType, setEmploymentType] = useState('')
  const canManage =
    me?.roles.some((role) => ['HiringManager', 'Admin'].includes(role)) ??
    false
  const normalizedSearch = search.trim().toLocaleLowerCase('sk-SK')
  const items = useMemo(
    () =>
      (data?.items ?? []).filter((post) => {
        const matchesSearch = [post.title, post.location].some((value) =>
          value.toLocaleLowerCase('sk-SK').includes(normalizedSearch),
        )
        return (
          matchesSearch &&
          (!employmentType || post.employmentType === employmentType)
        )
      }),
    [data?.items, employmentType, normalizedSearch],
  )

  return (
    <main className={styles.main}>
      <div className={styles.headingRow}>
        <div>
          <h1 className={styles.heading}>Pracovné ponuky</h1>
          <p className={styles.subtitle}>
            Spravujte koncepty, zverejnené a uzavreté pracovné pozície.
          </p>
        </div>
        {canManage && (
          <Button as="a" href="/staff/jobs/new" appearance="primary" icon={<AddRegular />}>
            Vytvoriť pracovnú pozíciu
          </Button>
        )}
      </div>
      {error && (
        <MessageBar className={styles.error} intent="error">
          <MessageBarBody>
            {error instanceof ApiError && error.status === 403
              ? 'Na správu pracovných ponúk nemáte oprávnenie.'
              : 'Pracovné ponuky sa nepodarilo načítať.'}
          </MessageBarBody>
        </MessageBar>
      )}
      <div className={styles.filters}>
        <div className={styles.search}>
          <SearchRegular aria-hidden="true" />
          <Input
            className={styles.filter}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Hľadať podľa názvu alebo lokality"
            aria-label="Hľadať pracovnú ponuku"
          />
        </div>
        <Select
          className={styles.filter}
          value={employmentType}
          onChange={(event) => setEmploymentType(event.target.value)}
          aria-label="Filtrovať podľa typu pracovného pomeru"
        >
          <option value="">Všetky typy úväzku</option>
          {employmentTypes.map((type) => (
            <option key={type} value={type}>
              {employmentTypeLabels[type]}
            </option>
          ))}
        </Select>
      </div>
      <div className={styles.tableWrapper}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.headerCell}>Názov pozície</th>
              <th className={styles.headerCell}>Lokalita</th>
              <th className={styles.headerCell}>Typ úväzku</th>
              <th className={styles.headerCell}>Stav</th>
            </tr>
          </thead>
          <tbody>
            {isPending && (
              <tr>
                <td className={styles.empty} colSpan={4}>Načítava sa…</td>
              </tr>
            )}
            {!isPending &&
              items.map((post) => (
                <tr key={post.id}>
                  <td className={styles.cell}>
                    <Link className={styles.title} to={`/staff/jobs/${post.id}`}>
                      {post.title}
                    </Link>
                  </td>
                  <td className={styles.cell}>{post.location}</td>
                  <td className={styles.cell}>
                    {employmentTypeLabels[post.employmentType]}
                  </td>
                  <td className={styles.cell}>
                    <span
                      className={`${styles.status} ${styles[post.status]}`}
                    >
                      {statusLabels[post.status]}
                    </span>
                  </td>
                </tr>
              ))}
            {!isPending && items.length === 0 && (
              <tr>
                <td className={styles.empty} colSpan={4}>
                  Nenašli sa žiadne pracovné ponuky.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </main>
  )
}
