import {
  Field,
  Input,
  makeStyles,
  Select,
  Textarea,
  tokens,
  type InputProps,
} from '@fluentui/react-components'
import { type UseQueryResult } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import {
  useWatch,
  type UseFormRegisterReturn,
  type UseFormReturn,
} from 'react-hook-form'
import { problemMessage } from '../../../shared/api/problem.ts'
import type { StaffJobPostForm } from '../jobPosts.ts'
import {
  employmentTypeLabels,
  employmentTypes,
  paymentTypeLabels,
  paymentTypes,
} from '../vocabulary.ts'

const useStyles = makeStyles({
  card: {
    display: 'grid',
    gap: '20px',
    padding: '20px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: tokens.shadow2,
  },
  cardHeading: {
    margin: 0,
    marginBottom: '-12px',
    color: tokens.colorNeutralForeground1,
    fontSize: '18px',
    lineHeight: '24px',
    fontWeight: 600,
  },
  // Two narrow columns aligned left, as in the design; the card around them stays full width.
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 300px))',
    columnGap: '16px',
    rowGap: '20px',
    // A validation message makes its field taller; the other one in the row keeps its height instead of stretching.
    alignItems: 'start',
    '@media (max-width: 700px)': {
      gridTemplateColumns: 'minmax(0, 1fr)',
    },
  },
  field: {
    display: 'grid',
    gap: '6px',
    alignContent: 'start',
    minWidth: 0,
  },
  label: {
    fontSize: '12px',
    fontWeight: 700,
    color: tokens.colorNeutralForeground1,
  },
  control: {
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
  },
  // A long option (e.g. „Dohoda o brigádnickej práci študentov“) is cut off instead of widening the field.
  select: {
    width: '100%',
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  description: {
    width: '100%',
    boxSizing: 'border-box',
    minHeight: '178px',
  },
})

type Person = { id: string; displayName: string }

/** A labelled field; the asterisk is part of the label, in its colour, as in the design. */
function FormField({
  label,
  error,
  children,
}: {
  label: string
  error?: string
  children: ReactNode
}) {
  const styles = useStyles()
  return (
    <Field
      className={styles.field}
      label={<span className={styles.label}>{label} *</span>}
      {...(error
        ? { validationState: 'error' as const, validationMessage: error }
        : {})}
    >
      {children}
    </Field>
  )
}

/**
 * A post's recruiter or hiring manager, chosen from the staff with that role. On the edit page, when the list does not
 * have the stored person — still loading, failed to load, or they are not (or no longer) staff with the role — the
 * field still shows them as its value, so it never looks empty and an edit never swaps them for someone else unasked,
 * but the open list offers only the people who can be picked.
 */
function StaffPicker({
  label,
  placeholder,
  people,
  currentId,
  value,
  disabled,
  registration,
  error,
}: {
  label: string
  placeholder: string
  people: UseQueryResult<Person[]>
  currentId?: string
  value: string | undefined
  disabled: boolean
  registration: UseFormRegisterReturn
  error?: string
}) {
  const styles = useStyles()
  const listed =
    !currentId || people.data?.some((person) => person.id === currentId)
  const fallback = people.isPending
    ? 'Načítava sa…'
    : people.isError
      ? 'Zoznam sa nepodarilo načítať'
      : 'Neznámy používateľ'

  return (
    <FormField
      label={label}
      error={people.isError ? problemMessage(people.error) : error}
    >
      <Select
        className={styles.control}
        select={{ className: styles.select }}
        required
        {...registration}
        disabled={disabled || !people.data}
        value={value ?? ''}
      >
        <option value="" disabled hidden>
          {people.isPending ? 'Načítava sa…' : placeholder}
        </option>
        {/* Shown as the closed field's value, never offered in the list: only people with the role can be picked. */}
        {!listed && (
          <option value={currentId} disabled hidden>
            {fallback}
          </option>
        )}
        {people.data?.map((person) => (
          <option key={person.id} value={person.id}>
            {person.displayName}
          </option>
        ))}
      </Select>
    </FormField>
  )
}

/**
 * The job post form's three cards — basic information, description and the people on the post — shared by the create
 * and edit pages. An admin also picks the hiring manager, next to the recruiter.
 */
export function JobPostFormFields({
  form,
  recruiters,
  hiringManagers,
  showHiringManager,
  disabled = false,
  current,
}: {
  form: UseFormReturn<StaffJobPostForm>
  recruiters: UseQueryResult<Person[]>
  hiringManagers: UseQueryResult<Person[]>
  showHiringManager: boolean
  disabled?: boolean
  /** The stored post's people, on the edit page. */
  current?: { recruiterId: string; hiringManagerId: string }
}) {
  const styles = useStyles()
  const {
    register,
    control,
    formState: { errors, defaultValues },
  } = form
  const [recruiterId, hiringManagerId] = useWatch({
    control,
    name: ['recruiterId', 'hiringManagerId'],
  })

  // Fluent's Input keeps its own value, so it starts from the form's default rather than from what the form writes
  // into the element; typing still reaches the form through `register`'s onChange.
  const input = (
    name: 'title' | 'location' | 'department' | 'salary' | 'openPositions',
    props: InputProps,
  ) => {
    const initial = defaultValues?.[name]
    return (
      <Input
        className={styles.control}
        required
        disabled={disabled}
        defaultValue={initial === undefined ? undefined : String(initial)}
        {...props}
        {...register(
          name,
          name === 'salary' || name === 'openPositions'
            ? { valueAsNumber: true }
            : {},
        )}
      />
    )
  }

  return (
    <>
      <section className={styles.card}>
        <h2 className={styles.cardHeading}>Základné informácie</h2>
        <div className={styles.grid}>
          <FormField
            label="Názov pracovnej pozície"
            error={errors.title?.message}
          >
            {input('title', { placeholder: 'Zadajte pracovnú pozíciu' })}
          </FormField>
          <FormField label="Lokalita" error={errors.location?.message}>
            {input('location', {
              placeholder: 'Zadajte lokalitu (napr. Bratislava)',
            })}
          </FormField>
          <FormField label="Oddelenie" error={errors.department?.message}>
            {input('department', { placeholder: 'Zadajte názov oddelenia' })}
          </FormField>
          <FormField
            label="Typ pracovného pomeru"
            error={errors.employmentType?.message}
          >
            <Select
              className={styles.control}
              select={{ className: styles.select }}
              required
              defaultValue=""
              disabled={disabled}
              {...register('employmentType')}
            >
              <option value="" disabled hidden>
                Vyberte typ pracovného pomeru
              </option>
              {employmentTypes.map((type) => (
                <option key={type} value={type}>
                  {employmentTypeLabels[type]}
                </option>
              ))}
            </Select>
          </FormField>
          <FormField label="Výška mzdy od (EUR)" error={errors.salary?.message}>
            {input('salary', {
              type: 'number',
              min: 0.01,
              step: 0.01,
              placeholder: 'Zadajte výšku mzdy',
            })}
          </FormField>
          <FormField label="Typ mzdy" error={errors.paymentType?.message}>
            <Select
              className={styles.control}
              select={{ className: styles.select }}
              required
              defaultValue=""
              disabled={disabled}
              {...register('paymentType')}
            >
              <option value="" disabled hidden>
                Vyberte typ mzdy
              </option>
              {paymentTypes.map((type) => (
                <option key={type} value={type}>
                  {paymentTypeLabels[type]}
                </option>
              ))}
            </Select>
          </FormField>
          <FormField
            label="Počet otvorených miest"
            error={errors.openPositions?.message}
          >
            {input('openPositions', {
              type: 'number',
              min: 1,
              max: 1000,
              step: 1,
              placeholder: 'Zadajte počet otvorených miest',
            })}
          </FormField>
        </div>
      </section>
      <section className={styles.card}>
        <FormField
          label="Opis pracovnej pozície"
          error={errors.description?.message}
        >
          <Textarea
            className={styles.description}
            placeholder="Zadajte opis pracovnej pozície…"
            resize="vertical"
            required
            disabled={disabled}
            {...register('description')}
          />
        </FormField>
      </section>
      <section className={styles.card}>
        <h2 className={styles.cardHeading}>Rozšírené nastavenia</h2>
        <div className={styles.grid}>
          <StaffPicker
            label="Vyberte recruitera"
            placeholder="Vyberte recruitera"
            people={recruiters}
            currentId={current?.recruiterId}
            value={recruiterId}
            disabled={disabled}
            registration={register('recruiterId')}
            error={errors.recruiterId?.message}
          />
          {showHiringManager && (
            <StaffPicker
              label="Vyberte prijímajúceho manažéra"
              placeholder="Vyberte prijímajúceho manažéra"
              people={hiringManagers}
              currentId={current?.hiringManagerId}
              value={hiringManagerId}
              disabled={disabled}
              registration={register('hiringManagerId')}
              error={errors.hiringManagerId?.message}
            />
          )}
        </div>
      </section>
    </>
  )
}
