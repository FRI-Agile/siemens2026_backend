import { Button, MessageBar, MessageBarBody } from '@fluentui/react-components'
import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { JobPostFormFields } from '../../../features/recruitment/jobPostForm/JobPostFormFields.tsx'
import { useJobPostPageStyles } from '../../../features/recruitment/jobPostForm/jobPostPageStyles.ts'
import {
  staffJobPostFormSchema,
  useCreateJobPost,
  useHiringManagers,
  useRecruiters,
  type CreateJobPostBody,
  type StaffJobPostForm,
} from '../../../features/recruitment/jobPosts.ts'
import {
  problemFieldErrors,
  problemMessage,
} from '../../../shared/api/problem.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

const formFields = [
  'title',
  'location',
  'department',
  'description',
  'employmentType',
  'salary',
  'paymentType',
  'openPositions',
  'recruiterId',
  'hiringManagerId',
] as const satisfies readonly (keyof StaffJobPostForm)[]

const hiringManagerRequired = 'Vyberte prijímajúceho manažéra.'

/** The roles the API lets create a job post; here they only shape the UI. */
const creatorRoles = ['HiringManager', 'Admin']

export function StaffJobsPage() {
  const styles = useJobPostPageStyles()
  const navigate = useNavigate()
  const createJobPost = useCreateJobPost()
  const { data: me } = useCurrentStaff()
  const recruiters = useRecruiters(me)
  const isAdmin = me?.roles.includes('Admin') ?? false
  const hiringManagers = useHiringManagers(isAdmin)
  const [saved, setSaved] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  // Remounts the fields after a save: Fluent's inputs keep their own text, which `reset()` alone does not clear.
  const [formKey, setFormKey] = useState(0)

  const canCreate =
    me?.roles.some((role) => creatorRoles.includes(role)) ?? false
  const busy = createJobPost.isPending || !canCreate

  const form = useForm<StaffJobPostForm>({
    resolver: zodResolver(staffJobPostFormSchema),
    defaultValues: {
      title: '',
      location: '',
      department: '',
      description: '',
    },
  })
  const { handleSubmit, reset, setError } = form

  function submit(
    values: StaffJobPostForm,
    status: CreateJobPostBody['status'],
  ) {
    setSaved(null)
    setFailure(null)
    // The shared schema knows nothing of roles; an admin must name the hiring manager, which the API also enforces.
    if (isAdmin && !values.hiringManagerId) {
      setError('hiringManagerId', { message: hiringManagerRequired })
      return
    }
    createJobPost.mutate(
      {
        ...values,
        hiringManagerId: isAdmin ? values.hiringManagerId : undefined,
        status,
      },
      {
        onSuccess: (post) => {
          reset()
          setFormKey((key) => key + 1)
          setSaved(
            post.status === 'published'
              ? `Pozícia „${post.title}“ bola zverejnená.`
              : `Pozícia „${post.title}“ bola uložená ako koncept.`,
          )
        },
        onError: (error) => {
          // The API's rules are authoritative (ADR-F010): its messages go onto the fields they name.
          const fields = problemFieldErrors(error)
          for (const name of formFields) {
            if (fields[name]) setError(name, { message: fields[name] })
          }
          setFailure(problemMessage(error))
        },
      },
    )
  }

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Vytvorenie pracovnej pozície</h1>
      <p className={styles.subtitle}>
        Vyplňte údaje potrebné na vytvorenie pracovnej pozície.
      </p>
      <form
        className={styles.form}
        noValidate
        onSubmit={(event) => {
          // Both buttons submit; the one pressed decides between a draft and a published post.
          const submitter = (event.nativeEvent as SubmitEvent).submitter
          const status =
            submitter?.getAttribute('name') === 'draft' ? 'draft' : 'published'
          void handleSubmit((values) => submit(values, status))(event)
        }}
      >
        {me && !canCreate && (
          <MessageBar intent="warning">
            <MessageBarBody>
              Pracovnú pozíciu môže vytvoriť iba HiringManager alebo Admin.
            </MessageBarBody>
          </MessageBar>
        )}
        {saved && (
          <MessageBar intent="success">
            <MessageBarBody>{saved}</MessageBarBody>
          </MessageBar>
        )}
        {failure && (
          <MessageBar intent="error">
            <MessageBarBody>{failure}</MessageBarBody>
          </MessageBar>
        )}
        <JobPostFormFields
          key={formKey}
          form={form}
          recruiters={recruiters}
          hiringManagers={hiringManagers}
          showHiringManager={isAdmin}
        />
        <div className={styles.actions}>
          <Button
            type="button"
            className={styles.button}
            disabled={createJobPost.isPending}
            onClick={() => void navigate('/staff/jobs')}
          >
            Zrušiť
          </Button>
          <Button
            type="submit"
            name="draft"
            className={styles.button}
            disabled={busy}
          >
            Uložiť koncept
          </Button>
          <Button
            type="submit"
            name="publish"
            appearance="primary"
            className={styles.button}
            disabled={busy}
          >
            Vytvoriť
          </Button>
        </div>
      </form>
    </div>
  )
}
