import {
  Button,
  Field,
  Input,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Select,
  Textarea,
} from '@fluentui/react-components'
import { ArrowLeftRegular } from '@fluentui/react-icons'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import {
  useCloseManagedJobPost,
  useManagedJobPost,
  useReopenManagedJobPost,
  useUpdateManagedJobPost,
} from '../../../features/recruitment/managedJobPosts.ts'
import {
  employmentTypeLabels,
  employmentTypes,
  paymentTypeLabels,
  paymentTypes,
} from '../../../features/recruitment/vocabulary.ts'
import {
  jobPostFormSchema,
  useRecruiters,
  type JobPostForm,
} from '../../../features/recruitment/jobPosts.ts'
import { problemMessage } from '../../../shared/api/problem.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

const useStyles = makeStyles({
  main: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '48px 32px',
  },
  back: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '8px',
    marginBottom: '28px',
    color: '#475569',
    textDecoration: 'none',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '32px',
  },
  subtitle: {
    margin: '8px 0 32px',
    color: '#475569',
  },
  section: {
    display: 'grid',
    gap: '20px',
    maxWidth: '1000px',
    padding: '28px',
    backgroundColor: '#FFFFFF',
    border: '1px solid #D1D5DB',
    borderRadius: '12px',
    boxShadow: '0 2px 8px rgba(31, 41, 51, 0.06)',
  },
  row: {
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    gap: '20px',
  },
  field: {
    display: 'grid',
    gap: '8px',
    alignContent: 'start',
  },
  input: {
    width: '100%',
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '12px',
    marginTop: '24px',
  },
  status: {
    display: 'inline-block',
    marginBottom: '24px',
    padding: '6px 12px',
    borderRadius: '999px',
    color: '#475569',
    backgroundColor: '#E8EBEF',
    fontWeight: 700,
  },
  error: {
    maxWidth: '1000px',
    marginBottom: '20px',
  },
  '@media (max-width: 700px)': {
    main: {
      padding: '36px 24px',
    },
    row: {
      gridTemplateColumns: '1fr',
    },
  },
})

const labels = {
  draft: 'Koncept',
  published: 'Zverejnená',
  closed: 'Uzavretá',
} as const

type EditableValues = JobPostForm

function getFormValues(post: {
  title: string
  location: string
  department: string
  description: string
  openPositions: number
  salary: number
  paymentType: JobPostForm['paymentType']
  employmentType: JobPostForm['employmentType']
  recruiterId: string
}): EditableValues {
  return {
    title: post.title,
    location: post.location,
    department: post.department,
    description: post.description,
    openPositions: post.openPositions,
    salary: post.salary,
    paymentType: post.paymentType,
    employmentType: post.employmentType,
    recruiterId: post.recruiterId,
  }
}

export function StaffJobPostDetailPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const { jobPostId = '' } = useParams()
  const { data: post, error, isPending } = useManagedJobPost(jobPostId)
  const { data: me } = useCurrentStaff()
  const recruiters = useRecruiters(me)
  const update = useUpdateManagedJobPost()
  const close = useCloseManagedJobPost()
  const reopen = useReopenManagedJobPost()
  const [editing, setEditing] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const canManage =
    me?.roles.some((role) => ['HiringManager', 'Admin'].includes(role)) ?? false
  const canEditRecruiter =
    me?.roles.some((role) => ['HiringManager', 'Admin'].includes(role)) ?? false

  const {
    control,
    register,
    reset,
    handleSubmit,
    formState: { errors },
  } = useForm<EditableValues>({
    resolver: zodResolver(jobPostFormSchema),
    values: useMemo(() => (post ? getFormValues(post) : undefined), [post]),
  })
  const formValues = useWatch({ control })

  if (isPending) return <main className={styles.main}>Načítava sa…</main>
  if (error) {
    return (
      <main className={styles.main}>
        <Link className={styles.back} to="/staff/jobs">
          <ArrowLeftRegular /> Späť na pracovné ponuky
        </Link>
        <MessageBar intent="error">
          <MessageBarBody>
            Pracovnú ponuku sa nepodarilo načítať.
          </MessageBarBody>
        </MessageBar>
      </main>
    )
  }
  if (!post) {
    return (
      <main className={styles.main}>
        <Link className={styles.back} to="/staff/jobs">
          <ArrowLeftRegular /> Späť na pracovné ponuky
        </Link>
        <p>Pracovná ponuka nebola nájdená.</p>
      </main>
    )
  }

  const fieldError = (name: keyof EditableValues) =>
    errors[name]?.message
      ? {
          validationState: 'error' as const,
          validationMessage: errors[name]?.message,
        }
      : {}

  const runCommand = (action: () => void) => {
    setFailure(null)
    action()
  }

  function submit(values: EditableValues) {
    if (!post) return
    runCommand(() =>
      update.mutate(
        {
          ...values,
          recruiterId: canEditRecruiter ? values.recruiterId : post.recruiterId,
          id: post.id,
          status: post.status === 'draft' ? 'draft' : 'published',
          version: post.version,
        },
        {
          onSuccess: () => {
            setEditing(false)
            navigate('/staff/jobs')
          },
          onError: (submitError) => setFailure(problemMessage(submitError)),
        },
      ),
    )
  }

  const busy = update.isPending || close.isPending || reopen.isPending

  return (
    <main className={styles.main}>
      <Link className={styles.back} to="/staff/jobs">
        <ArrowLeftRegular /> Späť na pracovné ponuky
      </Link>
      <h1 className={styles.heading}>{post.title}</h1>
      <p className={styles.subtitle}>Detail pracovnej ponuky</p>
      <span className={styles.status}>{labels[post.status]}</span>
      {failure && (
        <MessageBar className={styles.error} intent="error">
          <MessageBarBody>{failure}</MessageBarBody>
        </MessageBar>
      )}
      <form className={styles.section} onSubmit={handleSubmit(submit)}>
        <div className={styles.row}>
          <Field label="Názov pozície" required {...fieldError('title')}>
            <Input
              {...register('title')}
              disabled={!editing}
              value={editing ? formValues.title : post.title}
            />
          </Field>
          <Field label="Lokalita" required {...fieldError('location')}>
            <Input
              {...register('location')}
              disabled={!editing}
              value={editing ? formValues.location : post.location}
            />
          </Field>
        </div>
        <div className={styles.row}>
          <Field label="Oddelenie" required {...fieldError('department')}>
            <Input
              {...register('department')}
              disabled={!editing}
              value={editing ? formValues.department : post.department}
            />
          </Field>
          <Field label="Typ úväzku" required {...fieldError('employmentType')}>
            <Select disabled={!editing} {...register('employmentType')}>
              {employmentTypes.map((type) => (
                <option key={type} value={type}>
                  {employmentTypeLabels[type]}
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <div className={styles.row}>
          <Field label="Výška mzdy (EUR)" required {...fieldError('salary')}>
            <Input
              {...register('salary', { valueAsNumber: true })}
              type="number"
              step="0.01"
              min="0.01"
              disabled={!editing}
              value={String(editing ? formValues.salary : post.salary)}
            />
          </Field>
          <Field
            label="Frekvencia vyplácania"
            required
            {...fieldError('paymentType')}
          >
            <Select disabled={!editing} {...register('paymentType')}>
              {paymentTypes.map((type) => (
                <option key={type} value={type}>
                  {paymentTypeLabels[type]}
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <div className={styles.row}>
          <Field
            label="Počet otvorených miest"
            required
            {...fieldError('openPositions')}
          >
            <Input
              {...register('openPositions', { valueAsNumber: true })}
              type="number"
              min="1"
              disabled={!editing}
              value={String(
                editing ? formValues.openPositions : post.openPositions,
              )}
            />
          </Field>
          <Field label="Recruiter" required {...fieldError('recruiterId')}>
            <Select
              {...register('recruiterId')}
              disabled={!editing || !canEditRecruiter || recruiters.isPending}
              value={editing ? formValues.recruiterId : post.recruiterId}
            >
              {recruiters.data?.map((recruiter) => (
                <option key={recruiter.id} value={recruiter.id}>
                  {recruiter.displayName}
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <Field
          label="Opis pracovnej pozície"
          required
          {...fieldError('description')}
        >
          <Textarea rows={7} disabled={!editing} {...register('description')} />
        </Field>
        {canManage && post.status !== 'closed' && (
          <div className={styles.actions}>
            {!editing && (
              <Button
                type="button"
                onClick={() => setEditing(true)}
                disabled={busy}
              >
                Upraviť
              </Button>
            )}
            {editing && (
              <>
                <Button type="submit" appearance="primary" disabled={busy}>
                  Uložiť zmeny
                </Button>
                <Button
                  type="button"
                  onClick={() => {
                    reset(getFormValues(post))
                    setEditing(false)
                  }}
                  disabled={busy}
                >
                  Zrušiť
                </Button>
              </>
            )}
            {post.status === 'draft' && !editing && (
              <Button
                type="button"
                appearance="primary"
                disabled={busy}
                onClick={() =>
                  runCommand(() =>
                    update.mutate(
                      {
                        id: post.id,
                        title: post.title,
                        location: post.location,
                        department: post.department,
                        description: post.description,
                        status: 'published',
                        openPositions: post.openPositions,
                        salary: post.salary,
                        paymentType: post.paymentType,
                        employmentType: post.employmentType,
                        recruiterId: post.recruiterId,
                        version: post.version,
                      },
                      {
                        onSuccess: () => navigate('/staff/jobs'),
                        onError: (commandError) =>
                          setFailure(problemMessage(commandError)),
                      },
                    ),
                  )
                }
              >
                Zverejniť
              </Button>
            )}
            {post.status === 'published' && !editing && (
              <Button
                type="button"
                disabled={busy}
                onClick={() =>
                  runCommand(() =>
                    close.mutate(
                      { jobPostId: post.id, version: post.version },
                      {
                        onSuccess: () => navigate('/staff/jobs'),
                        onError: (commandError) =>
                          setFailure(problemMessage(commandError)),
                      },
                    ),
                  )
                }
              >
                Uzavrieť
              </Button>
            )}
          </div>
        )}
        {canManage && post.status === 'closed' && (
          <div className={styles.actions}>
            <Button
              type="button"
              appearance="primary"
              disabled={busy}
              onClick={() =>
                runCommand(() =>
                  reopen.mutate(
                    { jobPostId: post.id, version: post.version },
                    {
                      onSuccess: () => navigate('/staff/jobs'),
                      onError: (commandError) =>
                        setFailure(problemMessage(commandError)),
                    },
                  ),
                )
              }
            >
              Znovu otvoriť
            </Button>
          </div>
        )}
      </form>
    </main>
  )
}
