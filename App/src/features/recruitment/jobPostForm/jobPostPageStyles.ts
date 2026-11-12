import { makeStyles, tokens } from '@fluentui/react-components'

/** The create and edit pages' frame around the job post form: heading, subtitle, cards and the button row. */
export const useJobPostPageStyles = makeStyles({
  // A div, not <main>: the global `main` rule centres its content, and this page is aligned left as in the design.
  page: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '36px 28px 48px',
  },
  heading: {
    margin: 0,
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
  form: {
    display: 'grid',
    gap: '16px',
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '10px',
  },
  button: {
    fontWeight: 700,
  },
})
