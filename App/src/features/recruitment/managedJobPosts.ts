import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { request } from '../../shared/api/http.ts'
import { useCommand } from '../../shared/api/useCommand.ts'
import { getAccessToken } from '../../shared/auth/staffAuth.ts'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'
import type {
  EmploymentType,
  PaymentType,
} from './vocabulary.ts'

export type ManagedJobPostStatus = 'draft' | 'published' | 'closed'

export interface ManagedJobPost {
  id: string
  title: string
  location: string
  department: string
  description: string
  status: ManagedJobPostStatus
  openPositions: number
  salary: number
  paymentType: PaymentType
  employmentType: EmploymentType
  recruiterId: string
  createdAt: string
  createdBy: string
  updatedAt: string | null
  updatedBy: string | null
  version: number
}

interface ManagedJobPostPage {
  items: ManagedJobPost[]
  total: number
  offset: number
  limit: number
}

export interface UpdateManagedJobPostBody {
  id: string
  title: string
  location: string
  department: string
  description: string
  status: Exclude<ManagedJobPostStatus, 'closed'>
  openPositions: number
  salary: number
  paymentType: PaymentType
  employmentType: EmploymentType
  recruiterId: string
  version: number
}

type VersionCommand = { jobPostId: string; version: number }

export function useManagedJobPosts() {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['recruitment', 'job-posts', 'managed'],
    queryFn: async ({ signal }) =>
      request<ManagedJobPostPage>(
        '/api/v1/recruitment/job-posts/manage/search',
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ offset: 0, limit: 100 }),
          signal,
        },
      ),
    placeholderData: keepPreviousData,
  })
}

export function useManagedJobPost(id: string) {
  const query = useManagedJobPosts()
  return {
    ...query,
    data: query.data?.items.find((post) => post.id === id),
  }
}

export function useUpdateManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ id, ...body }: UpdateManagedJobPostBody, idempotencyKey) =>
      request<ManagedJobPost>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(id)}`,
        {
          method: 'PUT',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(body),
          idempotencyKey,
        },
      ),
  )
}

export function useCloseManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ jobPostId, version }: VersionCommand, idempotencyKey) =>
      request<StatusChangeResponse>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(jobPostId)}/close`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ version }),
          idempotencyKey,
        },
      ),
  )
}

export function useReopenManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ jobPostId, version }: VersionCommand, idempotencyKey) =>
      request<StatusChangeResponse>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(jobPostId)}/reopen`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ version }),
          idempotencyKey,
        },
      ),
  )
}

export interface StatusChangeResponse {
  id: string
  status: ManagedJobPostStatus
  updatedAt: string | null
  version: number
}
