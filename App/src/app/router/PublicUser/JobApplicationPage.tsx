import {
  Button,
  Field,
  Input,
  makeStyles,
  Text,
} from '@fluentui/react-components'
import { ArrowLeftRegular, DocumentAddRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'

const useStyles = makeStyles({
  main: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    width: '100%',
    boxSizing: 'border-box',
    padding: '64px 32px 88px',
  },
  back: {
    width: 'min(100%, 900px)',
    marginBottom: '32px',
  },
  card: {
    width: 'min(100%, 900px)',
    boxSizing: 'border-box',
    padding: '40px',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    backgroundColor: '#FFFFFF',
    boxShadow: '0 8px 24px rgba(31, 41, 51, 0.06)',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: 'clamp(28px, 4vw, 40px)',
    lineHeight: 1.15,
  },
  subtitle: {
    margin: '12px 0 32px',
    color: '#475569',
  },
  section: {
    display: 'grid',
    gap: '20px',
    marginTop: '24px',
    padding: '24px',
    border: '1px solid #E5E7EB',
    borderRadius: '10px',
  },
  sectionHeading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '21px',
  },
  fields: {
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    gap: '20px',
  },
  input: {
    width: '100%',
    boxSizing: 'border-box',
  },
  upload: {
    display: 'grid',
    gap: '8px',
    padding: '28px',
    border: '2px dashed #CBD5E1',
    borderRadius: '8px',
    backgroundColor: '#F8FAFC',
    textAlign: 'center',
  },
  fileInput: {
    width: '100%',
  },
  fileName: {
    color: '#475569',
  },
  error: {
    color: '#B3261E',
    fontSize: '13px',
  },
  fileList: {
    display: 'grid',
    gap: '8px',
    width: '100%',
  },
  fileRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '8px 12px',
    borderRadius: '6px',
    backgroundColor: '#FFFFFF',
  },
  documents: {
    display: 'grid',
    gap: '16px',
  },
  actions: {
    display: 'flex',
    justifyContent: 'center',
    gap: '12px',
    marginTop: '24px',
  },
  '@media (max-width: 700px)': {
    main: {
      padding: '40px 24px 64px',
    },
    card: {
      padding: '28px 24px',
    },
    fields: {
      gridTemplateColumns: '1fr',
    },
  },
})

export function JobApplicationPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const { offerId = '' } = useParams()
  const [files, setFiles] = useState<Record<string, File[]>>({})
  const [fileErrors, setFileErrors] = useState<Record<string, string>>({})
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const acceptedFileTypes = '.doc,.docx,.pdf,.jpg,.jpeg,.png'
  const maxDocumentBytes = 10 * 1024 * 1024
  const contentTypes: Record<string, string> = {
    pdf: 'application/pdf',
    doc: 'application/msword',
    docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    png: 'image/png',
  }

  function documentContentType(file: File) {
    const allowedTypes = new Set(Object.values(contentTypes))
    if (allowedTypes.has(file.type)) return file.type
    const extension = file.name.split('.').pop()?.toLowerCase() ?? ''
    return contentTypes[extension] ?? null
  }

  function selectFiles(
    documentType: string,
    selectedFiles: FileList | null,
    multiple: boolean,
  ) {
    const nextFiles = Array.from(selectedFiles ?? [])
    const existingFiles = Object.values(files).flat()
    if (
      multiple &&
      (files[documentType]?.length ?? 0) + nextFiles.length > 3
    ) {
      setFileErrors((current) => ({
        ...current,
        [documentType]: 'Môžete vybrať najviac 3 ostatné dokumenty.',
      }))
      return
    }
    const invalidFile = nextFiles.find((file) => {
      if (file.size > maxDocumentBytes) return 'size'
      if (!documentContentType(file)) return 'type'
      if (
        [...existingFiles, ...nextFiles].filter(
          (candidate) =>
            candidate.name === file.name &&
            candidate.size === file.size &&
            candidate.lastModified === file.lastModified,
        ).length > 1
      )
        return 'duplicate'
      return false
    })
    if (invalidFile) {
      const message =
        invalidFile.size > maxDocumentBytes
          ? 'Súbor môže mať najviac 10 MB.'
          : !documentContentType(invalidFile)
            ? 'Povolené sú iba súbory DOC, DOCX, PDF, JPG a PNG.'
            : 'Tento súbor už bol vybraný.'
      setFileErrors((current) => ({ ...current, [documentType]: message }))
      return
    }
    setFileErrors((current) => {
      return Object.fromEntries(
        Object.entries(current).filter(([key]) => key !== documentType),
      )
    })
    setFiles((current) => ({
      ...current,
      [documentType]: multiple
        ? [...(current[documentType] ?? []), ...nextFiles].slice(0, 3)
        : nextFiles.slice(0, 1),
    }))
  }

  function removeFile(documentType: string, fileIndex: number) {
    setFiles((current) => ({
      ...current,
      [documentType]: (current[documentType] ?? []).filter(
        (_, index) => index !== fileIndex,
      ),
    }))
  }
  function validateForm(form: HTMLFormElement) {
    const formData = new FormData(form)
    const nextErrors: Record<string, string> = {}
    const firstName = String(formData.get('firstName') ?? '').trim()
    const lastName = String(formData.get('lastName') ?? '').trim()
    const email = String(formData.get('email') ?? '').trim()
    const phone = String(formData.get('phoneNumber') ?? '').trim()
    if (firstName.length < 1 || firstName.length > 100)
      nextErrors.firstName = 'Meno musí mať 1 až 100 znakov.'
    if (lastName.length < 1 || lastName.length > 100)
      nextErrors.lastName = 'Priezvisko musí mať 1 až 100 znakov.'
    if (
      email.length > 254 ||
      !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)
    )
      nextErrors.email = 'Zadajte platnú e-mailovú adresu.'
    if (phone && !/^\+[1-9][0-9]{6,14}$/.test(phone))
      nextErrors.phoneNumber =
        'Telefón zadajte vo formáte +421905123456 bez medzier.'
    setFieldErrors(nextErrors)
    if (!files.resume?.length) {
      setFileErrors((current) => ({
        ...current,
        resume: 'Životopis je povinný.',
      }))
    }
    return Object.keys(nextErrors).length === 0 && Boolean(files.resume?.length)
  }
  const documentFields = [
    { type: 'resume', label: 'Životopis (max. 1 súbor)', multiple: false },
    {
      type: 'motivation',
      label: 'Motivačný list (max. 1 súbor)',
      multiple: false,
    },
    {
      type: 'other',
      label: 'Ostatné dokumenty (max. 3 súbory)',
      multiple: true,
    },
  ]

  return (
    <main className={styles.main}>
      <Button
        appearance="subtle"
        className={styles.back}
        icon={<ArrowLeftRegular />}
        onClick={() => void navigate(`/aktualne-pozicie/${offerId}`)}
      >
        Späť na pracovnú pozíciu
      </Button>
      <article className={styles.card}>
        <h1 className={styles.heading}>Podanie žiadosti na pracovnú pozíciu</h1>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            validateForm(event.currentTarget)
          }}
        >
          <section className={styles.section}>
            <h2 className={styles.sectionHeading}>Základné informácie o uchádzačovi</h2>
            <div className={styles.fields}>
              <Field label="Meno" required>
                <Input className={styles.input} name="firstName" placeholder="Zadajte meno" required />
                {fieldErrors.firstName && <Text className={styles.error}>{fieldErrors.firstName}</Text>}
              </Field>
              <Field label="Priezvisko" required>
                <Input className={styles.input} name="lastName" placeholder="Zadajte priezvisko" required />
                {fieldErrors.lastName && <Text className={styles.error}>{fieldErrors.lastName}</Text>}
              </Field>
              <Field label="E-mail" required>
                <Input className={styles.input} name="email" type="email" placeholder="Zadajte e-mail" required />
                {fieldErrors.email && <Text className={styles.error}>{fieldErrors.email}</Text>}
              </Field>
              <Field label="Telefónne číslo">
                <Input className={styles.input} name="phoneNumber" type="tel" placeholder="+421 ..." />
                {fieldErrors.phoneNumber && <Text className={styles.error}>{fieldErrors.phoneNumber}</Text>}
              </Field>
            </div>
          </section>
          <section className={styles.section}>
            <h2 className={styles.sectionHeading}>Dokumenty</h2>
            <div className={styles.documents}>
              {documentFields.map(({ type, label, multiple }) => (
                <label className={styles.upload} key={type}>
                  <Text weight="semibold">{label}</Text>
                  <DocumentAddRegular fontSize={28} />
                  <Text size={200}>DOC, DOCX, PDF, JPG alebo PNG (max. 10 MB)</Text>
                  <input
                    className={styles.fileInput}
                    type="file"
                    accept={acceptedFileTypes}
                    multiple={multiple}
                    disabled={multiple && (files[type]?.length ?? 0) >= 3}
                    onChange={(event) => {
                      selectFiles(type, event.target.files, multiple)
                      event.currentTarget.value = ''
                    }}
                  />
                  {files[type]?.length > 0 && (
                    <div className={styles.fileList}>
                      {files[type].map((file, index) => (
                        <div
                          className={styles.fileRow}
                          key={`${file.name}-${file.lastModified}-${index}`}
                        >
                          <Text className={styles.fileName}>{file.name}</Text>
                          <Button
                            type="button"
                            appearance="subtle"
                            onClick={(event) => {
                              event.preventDefault()
                              event.stopPropagation()
                              removeFile(type, index)
                            }}
                          >
                            Odstrániť
                          </Button>
                        </div>
                      ))}
                    </div>
                  )}
                  {fileErrors[type] && <Text className={styles.error}>{fileErrors[type]}</Text>}
                </label>
              ))}
            </div>
          </section>
          <div className={styles.actions}>
            <Button type="submit" appearance="primary">
              Odoslať
            </Button>
          </div>
        </form>
      </article>
    </main>
  )
}
