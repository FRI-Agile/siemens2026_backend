import { makeStyles, mergeClasses } from '@fluentui/react-components'
import {
  applicationStatusLabels,
  type ApplicationStatus,
} from './applications.ts'
import {
  managedJobPostStatusLabels,
  type ManagedJobPostStatus,
} from './managedJobPosts.ts'

const useStyles = makeStyles({
  badge: {
    display: 'inline-block',
    minWidth: '96px',
    boxSizing: 'border-box',
    padding: '6px 14px',
    borderRadius: '999px',
    fontSize: '12px',
    fontWeight: 600,
    textAlign: 'center',
    whiteSpace: 'nowrap',
  },
  grey: { color: '#5B6470', backgroundColor: '#E8EBEF' },
  yellow: { color: '#8A5A00', backgroundColor: '#FFF1D6' },
  green: { color: '#146C43', backgroundColor: '#D9F5E5' },
  teal: { color: '#0F766E', backgroundColor: '#D5F5EF' },
  blue: { color: '#1D4ED8', backgroundColor: '#E0EAFF' },
  red: { color: '#B42318', backgroundColor: '#FDE4E2' },
})

type Tone = Exclude<keyof ReturnType<typeof useStyles>, 'badge'>

const jobPostTones: Record<ManagedJobPostStatus, Tone> = {
  draft: 'yellow',
  published: 'green',
  closed: 'red',
}

const applicationTones: Record<ApplicationStatus, Tone> = {
  'nová žiadosť': 'grey',
  predvýber: 'blue',
  pohovor: 'yellow',
  ponuka: 'teal',
  prijatý: 'green',
  odmietnutý: 'red',
}

function Badge({ tone, children }: { tone: Tone; children: string }) {
  const styles = useStyles()
  return (
    <span className={mergeClasses(styles.badge, styles[tone])}>{children}</span>
  )
}

/** A job post's status as a coloured pill: a draft yellow, a published post green, a closed one red. */
export function JobPostStatusBadge({
  status,
}: {
  status: ManagedJobPostStatus
}) {
  return (
    <Badge tone={jobPostTones[status]}>
      {managedJobPostStatusLabels[status]}
    </Badge>
  )
}

/** An application's place in the pipeline as a coloured pill. */
export function ApplicationStatusBadge({
  status,
}: {
  status: ApplicationStatus
}) {
  return (
    <Badge tone={applicationTones[status] ?? 'grey'}>
      {applicationStatusLabels[status] ?? status}
    </Badge>
  )
}
