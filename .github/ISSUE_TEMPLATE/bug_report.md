name: Bug Report
description: Something in KSPCF isn't working correctly
labels: ["bug", "unconfirmed"]

body:
  - type: textarea
    id: repro-steps
    attributes:
      label: What did you do before the bug occurred?
      description: Ideally, provide a list of steps that will cause the bug to occur
    validations:
      required: true
  - type: textarea
    id: what-happened
    attributes:
      label: What happened?
      description: Ideally, include what you *expected* to happen
    validations:
      required: true
  - type: textarea
    id: log
    attributes:
      label: Upload your ksp.log file
      description: This file can be found adjacent to your ksp executable
    validations:
      required: true
  - type: textarea
    id: screenshot
    attributes:
      label: A screenshot of the problem
      description: Please include the whole KSP game window.  Video clips are great too!

