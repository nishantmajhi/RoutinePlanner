document.addEventListener("DOMContentLoaded", () => {
  const main = document.querySelector("main");
  const form = document.forms["routine"];
  const output = document.createElement("output");

  let isSubmitting = false;

  form.addEventListener("submit", async (event) => {
    event.preventDefault();

    if (isSubmitting) return;
    isSubmitting = true;

    output.innerHTML = "<p>⏳ Generating routine... please wait.</p>";
    main.appendChild(output);

    const rawFormData = new FormData(form);
    const formData = Object.fromEntries(rawFormData.entries());

    const selectedDays = Array.from(
      document.querySelectorAll("input[name='day']:checked")
    ).map((input) => input.value);

    const selectedSections = Array.from(
      document.querySelectorAll("input[name='sections']:checked")
    ).map((input) => input.value);

    const selectedSubjects = Array.from(
      document.querySelectorAll("input[name='subjects']:checked")
    ).map((input) => input.value);

    let periodsMap = {};

    selectedSections.forEach((section) => {
      const semesterNumber = parseInt(section[0]);
      const course = courses.find((c) => c.semester === semesterNumber);

      if (!course) return;

      const matchedSubjects = course.subjects
        .map((subject) => subject.code)
        .filter((code) => selectedSubjects.includes(code));

      periodsMap[section] = matchedSubjects;
    });

    const requestBody = {
      ...formData,
      days: selectedDays,
      periods: periodsMap,
      period_duration: Number(formData.period_duration),
      periods_per_subject: Number(formData.periods_per_subject),
    };

    fetch("/generate", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(requestBody),
    })
      .then((response) =>
        response
          .json()
          .then((data) => {
            if (response.ok) {
              window.open(
                `${window.location.origin}/routine/${data.id}`,
                "_self"
              );
            } else {
              output.innerHTML = `<p>❌ Error: ${
                data.error || "Unknown error"
              }</p>`;
            }
          })
          .catch(() => {
            output.innerHTML =
              "<p>❌ Server returned an invalid JSON response.</p>";
          })
      )
      .catch((err) => {
        console.error(err);
        output.innerHTML =
          "<p>🚨 An unexpected error occurred. Check the console for details.</p>";
      })
      .finally(() => {
        isSubmitting = false;
      });
  });
});