import {
  Button,
  makeStyles,
  MessageBar,
  MessageBarBody,
  tokens,
} from '@fluentui/react-components'
import {
  AddRegular,
  ChevronLeftRegular,
  EditRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import {
  applicationStatuses,
  applicationStatusLabels,
  useJobPostApplications,
  type ApplicationListItem,
} from '../../../features/recruitment/applications.ts'
import {
  FilterChecks,
  FilterChoice,
  JobPostListToolbar,
  JobPostPager,
  ListTable,
  type ListColumn,
} from '../../../features/recruitment/jobPostList/JobPostList.tsx'
import {
  listParam,
  sortOptions,
  useJobPostListParams,
} from '../../../features/recruitment/jobPostList/useJobPostListParams.ts'
import {
  canManageJobPost,
  useManagedJobPost,
} from '../../../features/recruitment/managedJobPosts.ts'
import {
  ApplicationStatusBadge,
  JobPostStatusBadge,
} from '../../../features/recruitment/StatusBadge.tsx'
import {
  employmentTypeLabels,
  formatEuros,
  paymentFrequencyLabels,
} from '../../../features/recruitment/vocabulary.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

const useStyles = makeStyles({
  // A div, not <main>: the global `main` rule centres its content, and this page is aligned left as in the design.
  page: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '28px 28px 48px',
  },
  back: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    padding: '5px 12px',
    color: tokens.colorNeutralForeground1,
    fontSize: '12px',
    fontWeight: 600,
    textDecoration: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  heading: {
    margin: '20px 0 0',
    color: tokens.colorNeutralForeground1,
    fontSize: '26px',
    lineHeight: '34px',
    fontWeight: 700,
  },
  subtitle: {
    margin: '14px 0 20px',
    color: tokens.colorNeutralForeground3,
    fontSize: '13px',
  },
  card: {
    display: 'grid',
    gap: '36px',
    padding: '36px 20px 40px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: tokens.shadow2,
  },
  titleRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '16px',
    padding: '0 16px',
  },
  title: {
    margin: 0,
    color: tokens.colorNeutralForeground1,
    fontSize: '38px',
    lineHeight: 1.2,
    fontWeight: 700,
  },
  titleActions: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
  },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '20px',
    margin: 0,
    padding: '0 20px',
    '@media (max-width: 700px)': {
      gridTemplateColumns: 'minmax(0, 1fr)',
    },
  },
  fact: {
    display: 'grid',
    gap: '6px',
    margin: 0,
  },
  factLabel: {
    color: tokens.colorNeutralForeground2,
    fontSize: '12px',
  },
  factValue: {
    margin: 0,
    color: tokens.colorNeutralForeground1,
    fontSize: '13px',
    fontWeight: 600,
  },
  description: {
    padding: '0 20px',
  },
  descriptionHeading: {
    margin: 0,
    color: tokens.colorNeutralForeground1,
    fontSize: '18px',
    fontWeight: 600,
  },
  descriptionText: {
    margin: '4px 0 0',
    color: tokens.colorNeutralForeground1,
    fontSize: '13px',
    lineHeight: 1.5,
    // The description is plain text; its line breaks are its paragraphs.
    whiteSpace: 'pre-line',
  },
  applicants: {
    display: 'grid',
    gap: '16px',
    paddingTop: '16px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  name: {
    color: tokens.colorNeutralForeground1,
    fontWeight: 600,
  },
})

function Fact({ label, children }: { label: string; children: ReactNode }) {
  const styles = useStyles()
  return (
    <div className={styles.fact}>
      <dt className={styles.factLabel}>{label}</dt>
      <dd className={styles.factValue}>{children}</dd>
    </div>
  )
}

export function StaffJobPostDetailPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const { jobPostId = '' } = useParams()
  const { data: post, error, isPending } = useManagedJobPost(jobPostId)
  const { data: me } = useCurrentStaff()

  const back = (
    <Link className={styles.back} to="/staff/jobs">
      <ChevronLeftRegular aria-hidden="true" /> Späť na pracovné pozície
    </Link>
  )

  if (isPending) {
    return (
      <div className={styles.page}>
        {back}
        <p className={styles.subtitle}>Načítava sa…</p>
      </div>
    )
  }
  if (error || !post) {
    return (
      <div className={styles.page}>
        {back}
        <h1 className={styles.heading}>Podrobnosti pracovnej pozície</h1>
        <MessageBar intent="error" style={{ marginTop: 20 }}>
          <MessageBarBody>
            {error
              ? 'Pracovnú pozíciu sa nepodarilo načítať.'
              : 'Pracovná pozícia nebola nájdená.'}
          </MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  return (
    <div className={styles.page}>
      {back}
      <h1 className={styles.heading}>Podrobnosti pracovnej pozície</h1>
      <p className={styles.subtitle}>
        Detailný popis pracovnej pozície a jej uchádzači.
      </p>
      <section className={styles.card} aria-labelledby="job-post-title">
        <div className={styles.titleRow}>
          <h2 id="job-post-title" className={styles.title}>
            {post.title}
          </h2>
          <div className={styles.titleActions}>
            <JobPostStatusBadge status={post.status} />
            {canManageJobPost(me, post) && (
              <Button
                icon={<EditRegular />}
                onClick={() => void navigate(`/staff/jobs/${post.id}/upravit`)}
              >
                Upraviť
              </Button>
            )}
          </div>
        </div>
        <dl className={styles.facts}>
          <Fact label="Lokalita">{post.location}</Fact>
          <Fact label="Typ úväzku">
            {employmentTypeLabels[post.employmentType]}
          </Fact>
          <Fact label="Oddelenie">{post.department}</Fact>
        </dl>
        <div className={styles.description}>
          <h3 className={styles.descriptionHeading}>
            Podrobnosti pracovnej pozície
          </h3>
          <p className={styles.descriptionText}>{post.description}</p>
        </div>
        <dl className={styles.facts}>
          <Fact label="Výška mzdy">od {formatEuros(post.salary)}</Fact>
          <Fact label="Frekvencia vyplácania">
            {paymentFrequencyLabels[post.paymentType]}
          </Fact>
          <Fact label="Počet otvorených miest">{post.openPositions}</Fact>
        </dl>
        <Applicants jobPostId={post.id} />
      </section>
    </div>
  )
}

/** Applicants per page. */
const PAGE_SIZE = 20

const statusOptions = applicationStatuses.map((status) => ({
  value: status,
  label: applicationStatusLabels[status],
}))

/** `+421905123456` as people read it, `+421 905 123 456`; another country's number as stored. */
function formatPhone(phone: string | null) {
  if (!phone) return '—'
  const slovak = /^\+421(\d{3})(\d{3})(\d{3})$/.exec(phone)
  return slovak ? `+421 ${slovak[1]} ${slovak[2]} ${slovak[3]}` : phone
}

/**
 * The post's applicants: searched by name (forgiving accents and a typo) or e-mail, filtered by state, newest or
 * oldest first, a page at a time. The search, filter and page are in the URL, like the job post list's.
 */
function Applicants({ jobPostId }: { jobPostId: string }) {
  const styles = useStyles()
  const { data: me } = useCurrentStaff()
  // Recruiters and admins add applicants by hand (roles-permissions.md §3); the API enforces it, here it shapes the UI.
  const canCreateApplicant =
    me?.roles.some((role) => ['Recruiter', 'Admin'].includes(role)) ?? false
  // The applicant list has no best-match sort, only newest or oldest first.
  const list = useJobPostListParams({ relevance: false })
  const statuses = listParam(list.params, 'stav', applicationStatuses)
  const { data, error, isPending, isPlaceholderData } = useJobPostApplications(
    jobPostId,
    {
      offset: (list.page - 1) * PAGE_SIZE,
      limit: PAGE_SIZE,
      search: list.search || undefined,
      statuses,
      sort: list.sort === 'oldest' ? 'oldest' : 'newest',
    },
  )

  const activeFilters = statuses.length + Number(list.sort !== 'newest')
  const message = isPending
    ? 'Načítava sa…'
    : error
      ? 'Uchádzačov sa nepodarilo načítať.'
      : data.items.length > 0
        ? undefined
        : data.total > 0
          ? 'Na tejto strane už nie sú žiadni uchádzači.'
          : list.search || statuses.length > 0
            ? 'Nenašli sa žiadni uchádzači.'
            : 'Na túto pozíciu sa zatiaľ nikto neprihlásil.'

  const columns: ListColumn<ApplicationListItem>[] = [
    {
      header: 'Meno',
      render: (a) => <span className={styles.name}>{a.firstName}</span>,
    },
    {
      header: 'Priezvisko',
      render: (a) => <span className={styles.name}>{a.lastName}</span>,
    },
    { header: 'E-mail', render: (a) => a.email },
    { header: 'Telefón', render: (a) => formatPhone(a.phoneNumber) },
    {
      header: 'Stav',
      render: (a) => <ApplicationStatusBadge status={a.status} />,
    },
  ]

  return (
    <div className={styles.applicants}>
      <JobPostListToolbar
        searchInput={list.searchInput}
        onSearchInput={list.setSearchInput}
        searchPlaceholder="Hľadať podľa mena alebo e-mailu"
        searchLabel="Hľadať uchádzačov"
        sticky={false}
        narrowSearch
        action={
          canCreateApplicant && (
            // TODO: open the manual candidate form once `POST …/applications` for staff exists (roles-permissions.md §7).
            <Button appearance="primary" size="large" icon={<AddRegular />}>
              Vytvoriť kandidáta
            </Button>
          )
        }
        activeFilters={activeFilters}
        onClearFilters={() => list.clearFilters({ stav: undefined })}
      >
        <FilterChecks
          label="Stav"
          values={statuses}
          options={statusOptions}
          onToggle={(status) =>
            list.toggle('stav', applicationStatuses, status)
          }
        />
        <FilterChoice
          label="Zoradiť"
          value={list.sort}
          options={sortOptions(false)}
          onChange={list.setSort}
        />
      </JobPostListToolbar>
      <ListTable
        columns={columns}
        items={data?.items ?? []}
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
    </div>
  )
}
