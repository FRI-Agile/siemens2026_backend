import { Button, makeStyles, mergeClasses } from '@fluentui/react-components'
import { AddRegular } from '@fluentui/react-icons'
import {
  FilterChecks,
  FilterChoice,
  JobPostListPage,
  JobPostListToolbar,
  JobPostPager,
  JobPostTable,
} from '../../../features/recruitment/jobPostList/JobPostList.tsx'
import {
  employmentTypeOptions,
  listParam,
  sortOptions,
  useJobPostListParams,
} from '../../../features/recruitment/jobPostList/useJobPostListParams.ts'
import {
  useManagedJobPosts,
  managedJobPostStatuses,
  type ManagedJobPostStatus,
} from '../../../features/recruitment/managedJobPosts.ts'
import { ApiError } from '../../../shared/api/http.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

const useStyles = makeStyles({
  status: {
    display: 'inline-block',
    padding: '7px 14px',
    borderRadius: '999px',
    fontSize: '13px',
    fontWeight: 700,
    whiteSpace: 'nowrap',
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
})

const statusLabels: Record<ManagedJobPostStatus, string> = {
  draft: 'Koncept',
  published: 'Zverejnená',
  closed: 'Uzavretá',
}

const statusOptions = managedJobPostStatuses.map((status) => ({
  value: status,
  label: statusLabels[status],
}))

/** Posts per page. */
const PAGE_SIZE = 20

export function StaffJobPostsPage() {
  const styles = useStyles()
  const list = useJobPostListParams()
  const statuses = listParam(list.params, 'stav', managedJobPostStatuses)
  const { data, error, isPending, isPlaceholderData } = useManagedJobPosts({
    offset: (list.page - 1) * PAGE_SIZE,
    limit: PAGE_SIZE,
    search: list.search || undefined,
    statuses,
    employmentTypes: list.employmentTypes,
    sort: list.sort,
  })
  const { data: me } = useCurrentStaff()
  const canManage =
    me?.roles.some((role) => ['HiringManager', 'Admin'].includes(role)) ?? false

  const activeFilters =
    statuses.length +
    list.employmentTypes.length +
    Number(list.sort !== list.defaultSort)
  const message = isPending
    ? 'Načítava sa…'
    : error
      ? error instanceof ApiError && error.status === 403
        ? 'Na správu pracovných ponúk nemáte oprávnenie.'
        : 'Pracovné ponuky sa nepodarilo načítať.'
      : data.items.length > 0
        ? undefined
        : data.total > 0
          ? 'Na tejto strane už nie sú žiadne pracovné ponuky.'
          : 'Nenašli sa žiadne pracovné ponuky.'

  return (
    <JobPostListPage
      heading="Pracovné ponuky"
      intro="Spravujte koncepty, zverejnené a uzavreté pracovné pozície."
      action={
        canManage && (
          <Button
            as="a"
            href="/staff/jobs/new"
            appearance="primary"
            size="large"
            icon={<AddRegular />}
          >
            Vytvoriť pracovnú pozíciu
          </Button>
        )
      }
    >
      <JobPostListToolbar
        searchInput={list.searchInput}
        onSearchInput={list.setSearchInput}
        activeFilters={activeFilters}
        onClearFilters={() => list.clearFilters({ stav: undefined })}
      >
        <FilterChecks
          label="Stav"
          values={statuses}
          options={statusOptions}
          onToggle={(status) =>
            list.toggle('stav', managedJobPostStatuses, status)
          }
        />
        <FilterChecks
          label="Typ pracovného pomeru"
          values={list.employmentTypes}
          options={employmentTypeOptions}
          onToggle={list.toggleEmploymentType}
        />
        <FilterChoice
          label="Zoradiť"
          value={list.sort}
          options={sortOptions(Boolean(list.search))}
          onChange={list.setSort}
        />
      </JobPostListToolbar>
      <JobPostTable
        items={data?.items ?? []}
        linkTo={(post) => `/staff/jobs/${post.id}`}
        extraColumn={{
          header: 'Stav',
          render: (post) => (
            <span className={mergeClasses(styles.status, styles[post.status])}>
              {statusLabels[post.status]}
            </span>
          ),
        }}
        message={message}
        busy={isPlaceholderData}
      />
      {data && (
        <JobPostPager
          page={list.page}
          offset={data.offset}
          shown={data.items.length}
          total={data.total}
          disabled={isPlaceholderData}
          onPage={list.setPage}
        />
      )}
    </JobPostListPage>
  )
}
