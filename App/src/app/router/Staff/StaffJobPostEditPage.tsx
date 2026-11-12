import { Button, MessageBar, MessageBarBody } from '@fluentui/react-components'
import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useParams } from 'react-router'
import { JobPostFormFields } from '../../../features/recruitment/jobPostForm/JobPostFormFields.tsx'
import { useJobPostPageStyles } from '../../../features/recruitment/jobPostForm/jobPostPageStyles.ts'
import {
  staffJobPostFormSchema,
  useHiringManagers,
  useRecruiters,
  type StaffJobPostForm,
} from '../../../features/recruitment/jobPosts.ts'
import {
  canManageJobPost,
  useCloseManagedJobPost,
  useManagedJobPost,
  useReopenManagedJobPost,
  useUpdateManagedJobPost,
  type ManagedJobPost,
} from '../../../features/recruitment/managedJobPosts.ts'
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

function getFormValues(post: ManagedJobPost): StaffJobPostForm {
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
    hiringManagerId: post.hiringManagerId,
  }
}

export function StaffJobPostEditPage() {
  const styles = useJobPostPageStyles()
  const { jobPostId = '' } = useParams()
  const { data: post, error, isPending } = useManagedJobPost(jobPostId)

  if (isPending) return <div className={styles.page}>Načítava sa…</div>
  if (error || !post) {
    return (
      <div className={styles.page}>
        <MessageBar intent="error">
          <MessageBarBody>
            {error
              ? 'Pracovnú pozíciu sa nepodarilo načítať.'
              : 'Pracovná pozícia nebola nájdená.'}
          </MessageBarBody>
        </MessageBar>
      </div>
    )
  }

  // Keyed by the post, so the form starts from the loaded values and another post never inherits its edits.
  return <EditJobPostForm key={post.id} post={post} />
}

function EditJobPostForm({ post }: { post: ManagedJobPost }) {
  const styles = useJobPostPageStyles()
  const navigate = useNavigate()
  const { data: me } = useCurrentStaff()
  const recruiters = useRecruiters(me)
  const isAdmin = me?.roles.includes('Admin') ?? false
  const hiringManagers = useHiringManagers(isAdmin)
  const update = useUpdateManagedJobPost()
  const close = useCloseManagedJobPost()
  const reopen = useReopenManagedJobPost()
  const [failure, setFailure] = useState<string | null>(null)

  const canManage = canManageJobPost(me, post)
  // A closed post is reopened before it is edited.
  const editable = canManage && post.status !== 'closed'
  const busy = update.isPending || close.isPending || reopen.isPending

  const form = useForm<StaffJobPostForm>({
    resolver: zodResolver(staffJobPostFormSchema),
    defaultValues: getFormValues(post),
  })
  const { handleSubmit, setError } = form

  // Back to the post's detail, where the edit started; a save, close or reopen shows it there too.
  const backToDetail = () => void navigate(`/staff/jobs/${post.id}`)
  const onFailure = (commandError: Error) =>
    setFailure(problemMessage(commandError))

  function submit(values: StaffJobPostForm, publish: boolean) {
    setFailure(null)
    update.mutate(
      {
        ...values,
        // Only an admin may change it; anyone else leaves it out and the post keeps its own.
        hiringManagerId: isAdmin ? values.hiringManagerId : undefined,
        id: post.id,
        status: publish || post.status !== 'draft' ? 'published' : 'draft',
        version: post.version,
      },
      {
        onSuccess: backToDetail,
        onError: (submitError) => {
          // The API's rules are authoritative (ADR-F010): its messages go onto the fields they name.
          const fields = problemFieldErrors(submitError)
          for (const name of formFields) {
            if (fields[name]) setError(name, { message: fields[name] })
          }
          onFailure(submitError)
        },
      },
    )
  }

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Upravenie pracovnej pozície</h1>
      <p className={styles.subtitle}>
        {editable
          ? 'Vyplňte údaje potrebné na upravenie pracovnej pozície.'
          : post.status === 'closed' && canManage
            ? 'Pozícia je uzavretá; ak ju chcete upraviť, najprv ju znovu otvorte.'
            : 'Údaje pracovnej pozície.'}
      </p>
      <form
        className={styles.form}
        noValidate
        onSubmit={(event) => {
          // Both buttons submit; „Zverejniť“ also publishes a draft.
          const submitter = (event.nativeEvent as SubmitEvent).submitter
          const publish = submitter?.getAttribute('name') === 'publish'
          void handleSubmit((values) => submit(values, publish))(event)
        }}
      >
        {failure && (
          <MessageBar intent="error">
            <MessageBarBody>{failure}</MessageBarBody>
          </MessageBar>
        )}
        <JobPostFormFields
          form={form}
          recruiters={recruiters}
          hiringManagers={hiringManagers}
          showHiringManager={isAdmin}
          disabled={!editable}
          current={post}
        />
        <div className={styles.actions}>
          <Button
            type="button"
            className={styles.button}
            disabled={busy}
            onClick={backToDetail}
          >
            {editable ? 'Zrušiť' : 'Späť'}
          </Button>
          {editable && (
            <Button
              type="submit"
              name="save"
              appearance="primary"
              className={styles.button}
              disabled={busy}
            >
              Upraviť
            </Button>
          )}
          {editable && post.status === 'draft' && (
            <Button
              type="submit"
              name="publish"
              className={styles.button}
              disabled={busy}
            >
              Zverejniť
            </Button>
          )}
          {editable && post.status === 'published' && (
            <Button
              type="button"
              className={styles.button}
              disabled={busy}
              onClick={() => {
                setFailure(null)
                close.mutate(
                  { jobPostId: post.id, version: post.version },
                  { onSuccess: backToDetail, onError: onFailure },
                )
              }}
            >
              Uzavrieť
            </Button>
          )}
          {canManage && post.status === 'closed' && (
            <Button
              type="button"
              appearance="primary"
              className={styles.button}
              disabled={busy}
              onClick={() => {
                setFailure(null)
                reopen.mutate(
                  { jobPostId: post.id, version: post.version },
                  { onSuccess: backToDetail, onError: onFailure },
                )
              }}
            >
              Znovu otvoriť
            </Button>
          )}
        </div>
      </form>
    </div>
  )
}
