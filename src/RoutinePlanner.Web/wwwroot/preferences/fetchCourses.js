let courses = [];

document.addEventListener("DOMContentLoaded", () => {
  fetch("/courses")
    .then((res) => {
      if (!res.ok) throw new Error("Failed to fetch courses");
      return res.json();
    })
    .then((data) => {
      courses = data.map((semester) => ({
        ...semester,
        subjects: semester.subjects.filter(
          (subject) =>
            !/seminar/i.test(subject.name) &&
            !/project/i.test(subject.name) &&
            !/research/i.test(subject.name)
        ),
      }));

      const semesterRadios = document.querySelectorAll(
        'input[name="semesters"]'
      );

      semesterRadios.forEach((radio) => {
        radio.addEventListener("change", () => generateSections(radio.value));
      });

      generateSections("Odd");
    })
    .catch((err) => {
      console.error("Error loading course data:", err);
      alert("Unable to load courses from the server!");
    });
});
