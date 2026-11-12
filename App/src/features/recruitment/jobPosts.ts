import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { request } from '../../shared/api/http.ts'
import { useCommand } from '../../shared/api/useCommand.ts'
import { getAccessToken } from '../../shared/auth/staffAuth.ts'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'
import type { CurrentStaff } from '../../shared/auth/useCurrentStaff.ts'
import { employmentTypes, paymentTypes } from './vocabulary.ts'

const required = 'Toto pole je povinné.'
const text = (max: number) =>
  z
    .string()
    .trim()
    .min(1, required)
    .max(max, `Text môže mať najviac ${max} znakov.`)
const positions = 'Zadajte celé číslo od 1 do 1000.'
const salary = 'Zadajte kladné číslo s najviac 2 desatinnými miestami.'

/** The job post form's rules; keep them in line with `CreateJobPostCommand` (ADR-F010). */
export const jobPostFormSchema = z.object({
  title: text(200),
  location: text(100),
  department: text(100),
  description: text(3000),
  employmentType: z.enum(employmentTypes, {
    error: 'Vyberte typ pracovného pomeru.',
  }),
  paymentType: z.enum(paymentTypes, { error: 'Vyberte typ mzdy.' }),
  openPositions: z
    .number({ error: positions })
    .int(positions)
    .min(1, positions)
    .max(1000, positions),
  // NUMERIC(10,2) in the database: compared in cents, since 0.1 + 0.2 is not exact in floating point.
  salary: z
    .number({ error: salary })
    .positive(salary)
    .max(99999999.99, salary)
    .refine(
      (value) => Math.abs(value * 100 - Math.round(value * 100)) < 1e-6,
      salary,
    ),
  // Undefined while the list is still loading, since a disabled select has no value.
  recruiterId: z
    .string({ error: 'Vyberte rekrútera.' })
    .min(1, 'Vyberte rekrútera.'),
})

export type JobPostForm = z.infer<typeof jobPostFormSchema>

/**
 * The create and edit forms' rules: the job post's, plus the hiring manager an admin names. Optional here, since a
 * hiring manager neither picks nor changes it; the pages require it from an admin.
 */
export const staffJobPostFormSchema = jobPostFormSchema.extend({
  hiringManagerId: z.string().optional(),
})

export type StaffJobPostForm = z.infer<typeof staffJobPostFormSchema>

/** Where a job post is in its life: `draft → published ⇄ closed` (`JobPostStatuses` in the API). */
export type JobPostStatus = 'draft' | 'published' | 'closed'

/** The body of `POST /api/v1/recruitment/job-posts`. TODO: replace with the generated OpenAPI client (ADR-F005). */
export interface CreateJobPostBody extends JobPostForm {
  /** `draft` saves a draft (koncept); a new post is never closed. */
  status: Exclude<JobPostStatus, 'closed'>
  /** Required from an admin; a hiring manager creates posts for themselves and leaves it out. */
  hiringManagerId?: string
}

/** A job post as the API returns it. */
export interface JobPost extends JobPostForm {
  status: JobPostStatus
  id: string
  hiringManagerId: string
  createdAt: string
  version: number
}

/** One entry of `GET /api/v1/recruitment/recruiters`. */
export interface Recruiter {
  id: string
  displayName: string
}

/** The staff with the Recruiter role, the people a job post's recruiter is chosen from. */
export function useRecruiters(currentStaff?: CurrentStaff) {
  const auth = useStaffAuth()
  const isRecruiter =
    currentStaff?.roles.includes('Recruiter') &&
    !currentStaff.roles.some((role) =>
      ['HiringManager', 'Admin'].includes(role),
    )

  return useQuery({
    queryKey: [
      'recruitment',
      'recruiters',
      isRecruiter ? currentStaff?.id : 'all',
    ],
    enabled: !!currentStaff,
    queryFn: async (): Promise<Recruiter[]> => {
      // A recruiter may not list the others; the only recruiter they can name is themselves.
      if (isRecruiter && currentStaff) {
        return [
          {
            id: currentStaff.id,
            displayName:
              currentStaff.name ?? currentStaff.username ?? currentStaff.id,
          },
        ]
      }

      const { users } = await request<{ users: Recruiter[] }>(
        '/api/v1/recruitment/recruiters',
        { headers: { Authorization: `Bearer ${await getAccessToken(auth)}` } },
      )
      return users
    },
    staleTime: 5 * 60 * 1000,
  })
}

/** One entry of `GET /api/v1/recruitment/hiring-managers`. */
export interface HiringManager {
  id: string
  displayName: string
}

/** The staff with the HiringManager role, the people an admin chooses a job post's hiring manager from. Admins only. */
export function useHiringManagers(enabled: boolean) {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['recruitment', 'hiring-managers'],
    enabled,
    queryFn: async (): Promise<HiringManager[]> => {
      const { users } = await request<{ users: HiringManager[] }>(
        '/api/v1/recruitment/hiring-managers',
        { headers: { Authorization: `Bearer ${await getAccessToken(auth)}` } },
      )
      return users
    },
    staleTime: 5 * 60 * 1000,
  })
}

/** `POST /api/v1/recruitment/job-posts`, with its idempotency key (ADR-A016). Hiring managers and admins only. */
export function useCreateJobPost() {
  const auth = useStaffAuth()

  return useCommand(async (body: CreateJobPostBody, idempotencyKey) =>
    request<JobPost>('/api/v1/recruitment/job-posts', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${await getAccessToken(auth)}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
      idempotencyKey,
    }),
  )
}
