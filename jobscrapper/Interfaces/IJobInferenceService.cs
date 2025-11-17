using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jobscrapper.Models;

namespace jobscrapper.Interfaces
{
    public interface IJobInferenceService
    {
        List<JobSample> LoadJsonl(string path);

        // --- Training ---
        void TrainCategoryModel(List<JobSample> data);
        void TrainAnswerModels(List<JobSample> data);

        // --- Save / Load ---
        void SaveCategoryModel(string path);
        void SaveCategoryAndAnswerModels(string folder);

        void LoadCategoryModel(string path);
        void LoadAnswerModels(string folder);

        // --- Inference ---
        string PredictJobCategory(string question);
        string PredictAnswer(string question);
    }
}
