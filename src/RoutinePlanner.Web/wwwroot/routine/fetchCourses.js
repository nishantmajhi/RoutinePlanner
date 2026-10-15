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

      (function () {
        const main = document.querySelector("main");
        main.innerHTML = "";

        const days = Object.keys(routine);
        const firstDaySlots = routine[days[0]].map(
          (s) =>
            `${convert24hrTo12hr(s.timeSlot.start)} - ${convert24hrTo12hr(
              s.timeSlot.end
            )}`
        );
        const sections = Object.keys(routine[days[0]][0].classes);

        sections.forEach((sectionName) => {
          const sectionEl = document.createElement("section");
          const heading = document.createElement("h3");
          heading.textContent = `Section - ${sectionName}`;
          sectionEl.appendChild(heading);

          const table = document.createElement("table");
          const thead = document.createElement("thead");
          const headerRow = document.createElement("tr");
          headerRow.appendChild(document.createElement("th")).textContent =
            "Day/Time";
          firstDaySlots.forEach((slot) => {
            const th = document.createElement("th");
            th.textContent = slot;
            headerRow.appendChild(th);
          });
          thead.appendChild(headerRow);
          table.appendChild(thead);

          const tbody = document.createElement("tbody");
          days.forEach((day) => {
            const row = document.createElement("tr");
            const dayCell = document.createElement("th");
            dayCell.textContent = day;
            row.appendChild(dayCell);

            routine[day].forEach((slot) => {
              const classData = slot.classes[sectionName];
              const cell = document.createElement("td");
              if (classData) {
                const subj = document.createElement("div");
                subj.className = "subject";

                const subjectObj = courses
                  .flatMap((sem) => sem.subjects)
                  .find((subj) => subj.code === classData.subject);

                subj.textContent = subjectObj
                  ? getAbbreviation(subjectObj.name)
                  : classData.subject;

                const teach = document.createElement("div");
                teach.className = "teacher";
                teach.textContent = classData.teacher;

                cell.appendChild(subj);
                cell.appendChild(teach);
              } else {
                cell.textContent = "—";
                cell.classList.add("empty");
              }
              row.appendChild(cell);
            });

            tbody.appendChild(row);
          });
          table.appendChild(tbody);
          sectionEl.appendChild(table);
          main.appendChild(sectionEl);
        });
      })();
    })
    .catch((err) => {
      console.error("Error loading course data:", err);
      alert("Unable to load courses from the server!");
    });
});
